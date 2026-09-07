using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services.Storage;

public sealed class WorkspaceManager : IWorkspaceManager
{
    private readonly string _workDirectory;

    public WorkspaceManager(
        string workDirectory)
    {
        _workDirectory = workDirectory;
    }

    public string CreateWorkingDirectory(
        string category)
    {
        var path =
            Path.Combine(
                _workDirectory,
                category,
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);

        return path;
    }

    public Task CleanupDirectoryAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(path) &&
            Directory.Exists(path))
        {
            Directory.Delete(
                path,
                recursive: true);
        }

        return Task.CompletedTask;
    }
}