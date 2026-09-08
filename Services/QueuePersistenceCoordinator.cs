using PS3Butler.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace PS3Butler.Services;

public sealed class QueuePersistenceCoordinator
{
    private readonly QueueStateService _queueStateService;
    private readonly ObservableCollection<GameJob> _jobs;
    private readonly Action<string>? _reportError;

    private bool _isRestoring;
    private bool _isStarted;

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
        if (_isRestoring)
        {
            return;
        }

        try
        {
            _queueStateService.Save(
                _jobs);
        }
        catch (Exception ex)
        {
            _reportError?.Invoke(
                $"Could not save queue: {ex.Message}");
        }
    }

    private void Jobs_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (GameJob job in e.OldItems)
            {
                job.PropertyChanged -=
                    Job_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (GameJob job in e.NewItems)
            {
                job.PropertyChanged +=
                    Job_PropertyChanged;
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
                nameof(GameJob.Status))
        {
            Save();
        }
    }
}
