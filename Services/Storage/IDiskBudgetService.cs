using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services.Storage;

public interface IDiskBudgetService
{
    Task<DiskBudgetResult> CheckAsync(
        string path,
        long requiredBytes,
        long safetyMarginBytes = 0,
        CancellationToken cancellationToken = default);
}