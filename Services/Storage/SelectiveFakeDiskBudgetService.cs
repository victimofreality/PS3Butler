using System;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services.Storage;

public sealed class SelectiveFakeDiskBudgetService : IDiskBudgetService
{
    private const long FakeAvailableBytes =
        1L * 1024 * 1024 * 1024;

    private readonly string _pathFragmentToFail;

    public SelectiveFakeDiskBudgetService(
        string pathFragmentToFail)
    {
        _pathFragmentToFail =
            pathFragmentToFail;
    }

    public Task<DiskBudgetResult> CheckAsync(
        string path,
        long requiredBytes,
        long safetyMarginBytes = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (path.Contains(
                _pathFragmentToFail,
                StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(
                DiskBudgetResult.Insufficient(
                    requiredBytes,
                    FakeAvailableBytes,
                    safetyMarginBytes,
                    $"TEST MODE: Pretending only 1.00 GiB is available at \"{path}\"."));
        }

        return Task.FromResult(
            DiskBudgetResult.Ok(
                requiredBytes,
                long.MaxValue,
                safetyMarginBytes));
    }
}