using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services.Storage;

public interface IWorkspaceManager
{
    string CreateWorkingDirectory(
        string category);

    Task CleanupDirectoryAsync(
        string? path,
        CancellationToken cancellationToken = default);
}