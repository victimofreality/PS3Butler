using PS3Butler.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public sealed class QueuePersistenceCoordinator
{
    private readonly IQueueStateStore _queueStateService;
    private readonly ObservableCollection<GameJob> _jobs;
    private readonly Action<string>? _reportError;

    private bool _isRestoring;
    private bool _isStarted;
    private bool _isStopped;
    private Task _saveTask = Task.CompletedTask;
    private Exception? _saveError;
    private readonly HashSet<GameJob> _trackedJobs = [];
    private readonly object _saveGate = new();
    private IReadOnlyList<QueueStateService.PersistedGameJob>? _pendingSnapshot;
    private bool _isWriterRunning;

    public QueuePersistenceCoordinator(
        IQueueStateStore queueStateService,
        ObservableCollection<GameJob> jobs,
        Action<string>? reportError = null)
    {
        _queueStateService =
            queueStateService;

        _jobs =
            jobs;

        _reportError =
            reportError;
    }

    public void Start()
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;

        _jobs.CollectionChanged +=
            Jobs_CollectionChanged;

        Restore();
    }

    private void Restore()
    {
        _isRestoring = true;

        try
        {
            foreach (var job in
                     _queueStateService.Load())
            {
                _jobs.Add(job);
            }
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private void Save()
    {
        if (_isRestoring || _isStopped)
        {
            return;
        }

        // Snapshot UI-bound objects before scheduling any background work.
        var snapshot = QueueStateService.CreateSnapshot(_jobs);
        QueueSnapshot(snapshot);
    }

    private void QueueSnapshot(IReadOnlyList<QueueStateService.PersistedGameJob> snapshot)
    {
        lock (_saveGate)
        {
            // Intermediate edits do not need separate disk writes. Retain at
            // most the active snapshot and the latest UI-thread snapshot.
            _pendingSnapshot = snapshot;
            if (!_isWriterRunning)
            {
                _isWriterRunning = true;
                _saveTask = SavePendingAsync();
            }
        }
    }

    private async Task SavePendingAsync()
    {
        try
        {
            while (true)
            {
                IReadOnlyList<QueueStateService.PersistedGameJob> snapshot;
                lock (_saveGate)
                {
                    if (_pendingSnapshot is null)
                    {
                        _isWriterRunning = false;
                        return;
                    }
                    snapshot = _pendingSnapshot;
                    _pendingSnapshot = null;
                }

                try
                {
                    await Task.Run(() => _queueStateService.SaveSnapshot(snapshot));
                    _saveError = null;
                }
                catch (Exception ex)
                {
                    _saveError = ex;
                    _reportError?.Invoke($"Could not save queue: {ex.Message}");
                }
            }
        }
        catch
        {
            // A throwing error reporter must not leave the writer permanently
            // marked active. The fault remains observable during shutdown.
            lock (_saveGate) _isWriterRunning = false;
            throw;
        }
    }

    public async Task StopAsync()
    {
        // Always write a final snapshot, including on a retry after a save failure.
        if (!_isStopped)
        {
            Save();
            _isStopped = true;
            _jobs.CollectionChanged -= Jobs_CollectionChanged;
            foreach (var job in _trackedJobs)
                job.PropertyChanged -= Job_PropertyChanged;
            _trackedJobs.Clear();
        }
        else if (_saveError is not null)
        {
            QueueSnapshot(QueueStateService.CreateSnapshot(_jobs));
        }

        await _saveTask;
        if (_saveError is not null)
            throw new InvalidOperationException("Could not save queue during shutdown. Close again to retry.", _saveError);
    }

    private void Jobs_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var job in _trackedJobs)
                job.PropertyChanged -= Job_PropertyChanged;
            _trackedJobs.Clear();
        }
        if (e.OldItems is not null)
        {
            foreach (GameJob job in e.OldItems)
            {
                job.PropertyChanged -=
                    Job_PropertyChanged;
                _trackedJobs.Remove(job);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (GameJob job in e.NewItems)
            {
                job.PropertyChanged +=
                    Job_PropertyChanged;
                _trackedJobs.Add(job);
            }
        }

        Save();
    }

    private void Job_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
                nameof(GameJob.OutputFormat) ||
            e.PropertyName ==
                nameof(GameJob.DestinationOverride) ||
            e.PropertyName ==
                nameof(GameJob.State))
        {
            Save();
        }
    }
}
