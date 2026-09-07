using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services.Execution;

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(
        ProcessRunRequest request,
        CancellationToken cancellationToken = default);
}