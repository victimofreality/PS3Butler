using PS3Butler.Services;

namespace PS3Butler.Tests.Services;

public class PauseTokenSourceTests
{
    [Fact]
    public async Task WaitWhilePausedAsync_CompletesAfterResume()
    {
        var source = new PauseTokenSource();
        source.Pause();

        var waitTask = source.WaitWhilePausedAsync();

        Assert.True(source.IsPaused);
        Assert.False(waitTask.IsCompleted);

        source.Resume();
        await waitTask.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(source.IsPaused);
    }

    [Fact]
    public async Task WaitWhilePausedAsync_ObservesCancellation()
    {
        var source = new PauseTokenSource();
        source.Pause();
        using var cancellation = new CancellationTokenSource();

        var waitTask = source.WaitWhilePausedAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await waitTask);
    }

    [Fact]
    public async Task WaitWhilePausedAsync_CompletesImmediatelyWhenRunning()
    {
        var source = new PauseTokenSource();

        await source.WaitWhilePausedAsync()
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(source.IsPaused);
    }
}
