using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class ArchiveExtractionService
{
    private readonly string _sevenZipPath;

    public ArchiveExtractionService(string sevenZipPath)
    {
        _sevenZipPath = sevenZipPath;
    }

    public async Task<(bool Success, string IsoPath, string Error)> ExtractIsoAsync(
        string archivePath,
        string outputDirectory)
    {
        if (!File.Exists(_sevenZipPath))
        {
            return (
                false,
                string.Empty,
                "7-Zip executable not found.");
        }

        if (!File.Exists(archivePath))
        {
            return (
                false,
                string.Empty,
                "Archive not found.");
        }

        Directory.CreateDirectory(outputDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = _sevenZipPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("x");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add($"-o{outputDirectory}");
        startInfo.ArgumentList.Add(archivePath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
        {
            var error =
                string.IsNullOrWhiteSpace(stderr)
                    ? stdout
                    : stderr;

            return (
                false,
                string.Empty,
                $"7-Zip exited with code {process.ExitCode}: {error.Trim()}");
        }

        var isoFiles = Directory
            .EnumerateFiles(
                outputDirectory,
                "*.iso",
                SearchOption.AllDirectories)
            .ToList();

        if (isoFiles.Count == 0)
        {
            return (
                false,
                string.Empty,
                "Extraction completed but no ISO was found.");
        }

        if (isoFiles.Count > 1)
        {
            return (
                false,
                string.Empty,
                "Extraction completed but multiple ISOs were found.");
        }

        return (
            true,
            isoFiles[0],
            string.Empty);
    }
}