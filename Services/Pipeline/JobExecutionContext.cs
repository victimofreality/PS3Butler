using PS3Butler.Models;
using System.Threading;

namespace PS3Butler.Services.Pipeline;

public sealed class JobExecutionContext
{
    public GameJob Job { get; }

    public CancellationToken CancellationToken { get; }

    public JobExecutionContext(
        GameJob job,
        CancellationToken cancellationToken)
    {
        Job = job;
        CancellationToken = cancellationToken;
    }
}