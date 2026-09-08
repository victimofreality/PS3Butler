using PS3Butler.Services.Execution;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class JbExtractionService
{
    private static readonly Regex ProgressRegex =
        new(
            @"(?<percent>\d{1,3})%",
            RegexOptions.Compiled);

    private readonly string _sevenZipPath;
    private readonly IProcessRunner _processRunner;

    public JbExtractionService(
        string sevenZipPath)
        : this(
            sevenZipPath,
            new ProcessRunner())
    {
    }

    public JbExtractionService(
        string sevenZipPath,
        IProcessRunner processRunner)
    {
        _sevenZipPath = sevenZipPath;
        _processRunner = processRunner;
    }

    public async Task<(bool Success, string Error)> ExtractAsync(
        string decryptedIsoPath,
        string outputDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_sevenZipPath))
        {
            return (
                false,
                "7-Zip executable not found.");
        }

        if (!File.Exists(decryptedIsoPath))
        {
            return (
                false,
                "Decrypted ISO not found.");
        }

        Directory.CreateDirectory(
            outputDirectory);

        var pendingText =
            string.Empty;

        void HandleOutputChunk(
            string chunk)
        {
            pendingText += chunk;

            foreach (Match match in
                     ProgressRegex.Matches(
                         pendingText))
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
                    $"\"{decryptedIsoPath}\"",

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
                "JB extraction cancelled.");
        }

        if (result.TimedOut)
        {
            return (
                false,
                "JB extraction timed out.");
        }

        var ps3GameDirectory =
            Path.Combine(
                outputDirectory,
                "PS3_GAME");

        var paramSfoPath =
            Path.Combine(
                ps3GameDirectory,
                "PARAM.SFO");

        if (!Directory.Exists(ps3GameDirectory) ||
            !File.Exists(paramSfoPath))
        {
            return (
                false,
                $"JB extraction did not produce a valid PS3_GAME structure. " +
                $"7-Zip exit code: {result.ExitCode}. " +
                $"{result.StandardError.Trim()}");
        }

        /*
         * 7-Zip can report a non-zero exit code / "Headers Error"
         * when extracting otherwise usable decrypted PS3 ISOs.
         *
         * If PS3_GAME and PARAM.SFO were extracted successfully,
         * accept the result rather than treating the UDF warning
         * as a fatal extraction failure.
         */
        if (!result.Success)
        {
            var knownPs3HeaderWarning =
                result.StandardError.Contains(
                    "Headers Error",
                    StringComparison.OrdinalIgnoreCase);

            if (!knownPs3HeaderWarning)
            {
                return (
                    false,
                    $"7-Zip exited with code {result.ExitCode}: " +
                    $"{result.StandardError.Trim()}");
            }
        }

        progress?.Report(
            100);

        return (
            true,
            string.Empty);
    }
}