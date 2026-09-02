using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class PauseTokenSource
{
    private volatile TaskCompletionSource<bool>? _paused;

    public bool IsPaused =>
        _paused is not null;

    public void Pause()
    {
        if (_paused is not null)
        {
            return;
        }

        _paused =
            new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public void Resume()
    {
        var paused =
            _paused;

        if (paused is null)
        {
            return;
        }

        _paused = null;

        paused.TrySetResult(true);
    }

    public async Task WaitWhilePausedAsync(
        CancellationToken cancellationToken = default)
    {
        var paused =
            _paused;

        if (paused is null)
        {
            return;
        }

        await paused.Task.WaitAsync(
            cancellationToken);
    }
}