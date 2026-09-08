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
                "ps3decrs executable not found.");
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

        if (File.Exists(expectedOutputPath))
        {
            File.Delete(
                expectedOutputPath);
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
         * ps3decrs does not currently give us convenient structured
         * percentage progress, so preserve the existing behaviour:
         * poll the output file size while the process is running.
         */
        while (!processTask.IsCompleted)
        {
            if (File.Exists(expectedOutputPath) &&
                inputSize > 0)
            {
                try
                {
                    var outputSize =
                        new FileInfo(
                            expectedOutputPath).Length;

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
                     * ps3decrs is writing to it. Try again on the
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
                $"ps3decrs exited with code {result.ExitCode}: " +
                $"{error.Trim()}");
        }

        if (!File.Exists(expectedOutputPath))
        {
            return (
                false,
                string.Empty,
                $"Decryption completed but expected output was not found: " +
                $"{expectedOutputPath}");
        }

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
        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(
                    outputPath);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}