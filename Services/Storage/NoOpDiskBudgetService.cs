using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services.Storage;

public sealed class NoOpDiskBudgetService : IDiskBudgetService
{
    public Task<DiskBudgetResult> CheckAsync(
        string path,
        long requiredBytes,
        long safetyMarginBytes = 0,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            DiskBudgetResult.Ok(
                requiredBytes,
                long.MaxValue,
                safetyMarginBytes));
    }
}