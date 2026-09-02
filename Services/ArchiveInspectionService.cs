using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class ArchiveInspectionService
{
    private readonly string _sevenZipPath;

    public ArchiveInspectionService(string sevenZipPath)
    {
        _sevenZipPath = sevenZipPath;
    }

    public async Task<string?> FindIsoNameAsync(string archivePath)
    {
        if (!File.Exists(_sevenZipPath))
        {
            return null;
        }

        if (!File.Exists(archivePath))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _sevenZipPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("l");
        startInfo.ArgumentList.Add("-slt");
        startInfo.ArgumentList.Add(archivePath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            return null;
        }

        var isoPaths = output
            .Split(Environment.NewLine)
            .Where(line => line.StartsWith("Path = ", StringComparison.Ordinal))
            .Select(line => line["Path = ".Length..].Trim())
            .Where(path =>
                path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (isoPaths.Count != 1)
        {
            return null;
        }

        return Path.GetFileName(isoPaths[0]);
    }
}