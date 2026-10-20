using PS3Butler.Services.Execution;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class ArchiveInspectionService
{
    private readonly string _sevenZipPath;
    private readonly IProcessRunner _processRunner;

    public ArchiveInspectionService(
        string sevenZipPath)
        : this(
            sevenZipPath,
            new ProcessRunner())
    {
    }

    public ArchiveInspectionService(
        string sevenZipPath,
        IProcessRunner processRunner)
    {
        _sevenZipPath = sevenZipPath;
        _processRunner = processRunner;
    }

    public async Task<string?> FindIsoNameAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_sevenZipPath))
        {
            return null;
        }

        if (!File.Exists(archivePath))
        {
            return null;
        }

        var request =
            new ProcessRunRequest
            {
                FileName = _sevenZipPath,
                Arguments =
                    $"l -slt \"{archivePath}\"",
                Timeout =
                    TimeSpan.FromSeconds(30)
            };

        var result =
            await _processRunner.RunAsync(
                request,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (!result.Success)
        {
            return null;
        }

        var isoPaths =
            result.StandardOutput
                .Split(
                    Environment.NewLine,
                    StringSplitOptions.RemoveEmptyEntries)
                .Where(line =>
                    line.StartsWith(
                        "Path = ",
                        StringComparison.Ordinal))
                .Select(line =>
                    line["Path = ".Length..].Trim())
                .Where(path =>
                    path.EndsWith(
                        ".iso",
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (isoPaths.Count != 1)
        {
            return null;
        }

        return Path.GetFileName(
            isoPaths[0]);
    }
}
