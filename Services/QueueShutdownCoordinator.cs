using System;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public sealed class QueueShutdownCoordinator
{
    private readonly Action _cancel;
    private readonly Func<Task> _waitForOperations;
    private readonly Func<Task> _stopPersistence;
    private Task? _shutdownTask;

    public bool IsStopping { get; private set; }

    public QueueShutdownCoordinator(Action cancel, Func<Task> waitForOperations, Func<Task> stopPersistence)
    {
        _cancel = cancel;
        _waitForOperations = waitForOperations;
        _stopPersistence = stopPersistence;
    }

    public Task StopAsync()
    {
        IsStopping = true;
        if (_shutdownTask is null || _shutdownTask.IsFaulted)
            _shutdownTask = StopCoreAsync();
        return _shutdownTask;
    }

    private async Task StopCoreAsync()
    {
        _cancel();
        await _waitForOperations();
        await _stopPersistence();
    }
}
