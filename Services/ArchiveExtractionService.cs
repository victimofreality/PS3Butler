using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class ArchiveExtractionService
{
    private static readonly Regex ProgressRegex =
        new(@"(?<percent>\d{1,3})%",
            RegexOptions.Compiled);

    private readonly string _sevenZipPath;

    public ArchiveExtractionService(string sevenZipPath)
    {
        _sevenZipPath = sevenZipPath;
    }

    public async Task<(bool Success, string IsoPath, string Error)> ExtractIsoAsync(
        string archivePath,
        string outputDirectory,
        IProgress<double>? progress = null)
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
        startInfo.ArgumentList.Add("-bsp1");
        startInfo.ArgumentList.Add($"-o{outputDirectory}");
        startInfo.ArgumentList.Add(archivePath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        var stderrTask =
            process.StandardError.ReadToEndAsync();

        var outputBuffer =
            new char[256];

        var pendingText =
            string.Empty;

        while (true)
        {
            var charsRead =
                await process.StandardOutput.ReadAsync(
                    outputBuffer.AsMemory(
                        0,
                        outputBuffer.Length));

            if (charsRead == 0)
            {
                break;
            }

            pendingText +=
                new string(
                    outputBuffer,
                    0,
                    charsRead);

            var matches =
                ProgressRegex.Matches(
                    pendingText);

            foreach (Match match in matches)
            {
                if (double.TryParse(
                        match.Groups["percent"].Value,
                        out var percentage))
                {
                    progress?.Report(
                        Math.Clamp(
                            percentage,
                            0,
                            100));
                }
            }

            if (pendingText.Length > 1024)
            {
                pendingText =
                    pendingText[^512..];
            }
        }

        await process.WaitForExitAsync();

        var stderr =
            await stderrTask;

        if (process.ExitCode != 0)
        {
            return (
                false,
                string.Empty,
                $"7-Zip exited with code {process.ExitCode}: {stderr.Trim()}");
        }

        var isoFiles =
            Directory
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

        progress?.Report(100);

        return (
            true,
            isoFiles[0],
            string.Empty);
    }
}