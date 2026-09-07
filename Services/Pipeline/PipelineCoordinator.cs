using PS3Butler.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services.Pipeline;

public sealed class PipelineCoordinator
{
    public async Task ExecuteAsync(
        IReadOnlyList<GameJob> jobs,
        Func<GameJob, CancellationToken, Task> processJobAsync,
        CancellationToken cancellationToken = default)
    {
        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await processJobAsync(
                job,
                cancellationToken);
        }
    }
}