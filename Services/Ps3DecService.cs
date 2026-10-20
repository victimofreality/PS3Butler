using PS3Butler.Services.Execution;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class Ps3DecService
{
    private readonly string _executablePath;
    private readonly int _threadCount;
    private readonly IProcessRunner _processRunner;

    public Ps3DecService(
        string executablePath,
        int threadCount)
        : this(
            executablePath,
            threadCount,
            new ProcessRunner())
    {
    }

    public Ps3DecService(
        string executablePath,
        int threadCount,
        IProcessRunner processRunner)
    {
        _executablePath = executablePath;
        _threadCount = threadCount;
        _processRunner = processRunner;
    }

    public async Task<(bool Success, string OutputPath, string Error)> DecryptAsync(
        string inputIsoPath,
        string discKey,
        string outputDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_executablePath))
        {
            return (
                false,
                string.Empty,
                "Redrrx PS3Dec executable not found (ps3dec.exe or ps3decrs.exe).");
        }

        if (!File.Exists(inputIsoPath))
        {
            return (
                false,
                string.Empty,
                "Input ISO not found.");
        }

        Directory.CreateDirectory(
            outputDirectory);

        var outputName =
            Path.GetFileNameWithoutExtension(inputIsoPath) +
            ".decrypted";

        var expectedOutputPath =
            Path.Combine(
                outputDirectory,
                outputName + ".iso");

        // Butler starts fresh rather than retaining decryptor recovery state.
        // v3 adds these sidecars; older Redrrx builds only write the ISO.
        foreach (var path in new[] { expectedOutputPath, expectedOutputPath + ".part", expectedOutputPath + ".resume" })
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        var inputSize =
            new FileInfo(
                inputIsoPath).Length;

        var request =
            new ProcessRunRequest
            {
                FileName =
                    _executablePath,

                ArgumentList =
    new[]
    {
        "--dk",
        discKey,

        "--tc",
        _threadCount.ToString(),

        "--output-dir",
        outputDirectory,

        "--output-name",
        outputName,

        "--skip",
        inputIsoPath
    }

            };

        /*
         * Do not configure an arbitrary timeout here.
         *
         * Decrypting a large PS3 ISO can legitimately take a long
         * time. User cancellation is what should terminate it.
         */
        var processTask =
            _processRunner.RunAsync(
                request,
                cancellationToken);

        /*
         * Redrrx PS3Dec does not currently give us convenient structured
         * percentage progress, so preserve the existing behaviour:
         * poll the output file size while the process is running.
         */
        while (!processTask.IsCompleted)
        {
            var progressPath = File.Exists(expectedOutputPath + ".part")
                ? expectedOutputPath + ".part"
                : expectedOutputPath;

            if (File.Exists(progressPath) &&
                inputSize > 0)
            {
                try
                {
                    var outputSize =
                        new FileInfo(
                            progressPath).Length;

                    var percentage =
                        outputSize *
                        100.0 /
                        inputSize;

                    progress?.Report(
                        Math.Clamp(
                            percentage,
                            0,
                            99.9));
                }
                catch (IOException)
                {
                    /*
                     * The file may briefly be unavailable while
                     * the decryptor is writing to it. Try again on the
                     * next poll.
                     */
                }
            }

            await Task.Delay(100);
        }

        var result =
            await processTask;

        if (result.WasCancelled)
        {
            DeletePartialOutput(
                expectedOutputPath);

            return (
                false,
                string.Empty,
                "Decryption cancelled.");
        }

        if (result.TimedOut)
        {
            DeletePartialOutput(
                expectedOutputPath);

            return (
                false,
                string.Empty,
                "Decryption timed out.");
        }

        if (!result.Success)
        {
            DeletePartialOutput(
                expectedOutputPath);

            var error =
                string.IsNullOrWhiteSpace(
                    result.StandardError)
                    ? result.StandardOutput
                    : result.StandardError;

            return (
                false,
                string.Empty,
                $"Redrrx PS3Dec exited with code {result.ExitCode}: " +
                $"{error.Trim()}");
        }

        if (!File.Exists(expectedOutputPath))
        {
            DeletePartialOutput(expectedOutputPath);
            return (
                false,
                string.Empty,
                $"Decryption completed but expected output was not found: " +
                $"{expectedOutputPath}");
        }

        // v3 keeps a checkpoint after success. Butler owns the work lifecycle.
        TryDeleteFile(expectedOutputPath + ".resume");

        progress?.Report(
            100);

        return (
            true,
            expectedOutputPath,
            string.Empty);
    }

    private static void DeletePartialOutput(
        string outputPath)
    {
        foreach (var path in new[] { outputPath, outputPath + ".part", outputPath + ".resume" })
        {
            TryDeleteFile(path);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(
                    path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
