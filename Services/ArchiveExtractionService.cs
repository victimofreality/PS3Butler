using PS3Butler.Services.Execution;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class ArchiveExtractionService
{
    private static readonly Regex ProgressRegex =
        new(
            @"(?<percent>\d{1,3})%",
            RegexOptions.Compiled);

    private readonly string _sevenZipPath;
    private readonly IProcessRunner _processRunner;

    public ArchiveExtractionService(
        string sevenZipPath)
        : this(
            sevenZipPath,
            new ProcessRunner())
    {
    }

    public ArchiveExtractionService(
        string sevenZipPath,
        IProcessRunner processRunner)
    {
        _sevenZipPath = sevenZipPath;
        _processRunner = processRunner;
    }

    public async Task<(bool Success, string IsoPath, string Error)> ExtractIsoAsync(
        string archivePath,
        string outputDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
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

        Directory.CreateDirectory(
            outputDirectory);

        var pendingText =
            string.Empty;

        void HandleOutputChunk(
            string chunk)
        {
            pendingText += chunk;

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

        var request =
            new ProcessRunRequest
            {
                FileName =
                    _sevenZipPath,

                Arguments =
                    $"x -y -bsp1 " +
                    $"-o\"{outputDirectory}\" " +
                    $"\"{archivePath}\"",

                StandardOutputChunkReceived =
                    HandleOutputChunk
            };

        var result =
            await _processRunner.RunAsync(
                request,
                cancellationToken);

        if (result.WasCancelled)
        {
            return (
                false,
                string.Empty,
                "Archive extraction cancelled.");
        }

        if (result.TimedOut)
        {
            return (
                false,
                string.Empty,
                "Archive extraction timed out.");
        }

        if (!result.Success)
        {
            return (
                false,
                string.Empty,
                $"7-Zip exited with code {result.ExitCode}: " +
                $"{result.StandardError.Trim()}");
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

        progress?.Report(
            100);

        return (
            true,
            isoFiles[0],
            string.Empty);
    }
}