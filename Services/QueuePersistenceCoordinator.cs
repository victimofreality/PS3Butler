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
    private readonly QueueStateService _queueStateService;
    private readonly ObservableCollection<GameJob> _jobs;
    private readonly Action<string>? _reportError;

    private bool _isRestoring;
    private bool _isStarted;
    private bool _isStopped;
    private Task _saveTask = Task.CompletedTask;
    private Exception? _saveError;
    private readonly HashSet<GameJob> _trackedJobs = [];

    public QueuePersistenceCoordinator(
        QueueStateService queueStateService,
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
        var previousSave = _saveTask;
        _saveTask = SaveAsync(previousSave, snapshot);
    }

    private async Task SaveAsync(Task previousSave, IReadOnlyList<QueueStateService.PersistedGameJob> snapshot)
    {
        await previousSave;
        try
        {
            await Task.Run(() => _queueStateService.SaveSnapshot(snapshot));
            _saveError = null;
        }
        catch (Exception ex)
        {
            _saveError = ex;
            _reportError?.Invoke(
                $"Could not save queue: {ex.Message}");
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
            _saveTask = SaveAsync(_saveTask, QueueStateService.CreateSnapshot(_jobs));
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
