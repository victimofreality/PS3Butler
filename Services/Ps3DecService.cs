using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class Ps3DecService
{
    private readonly string _executablePath;
    private readonly int _threadCount;

    public Ps3DecService(
        string executablePath,
        int threadCount)
    {
        _executablePath = executablePath;
        _threadCount = threadCount;
    }

    public async Task<(bool Success, string OutputPath, string Error)> DecryptAsync(
        string inputIsoPath,
        string discKey,
        string outputDirectory,
        IProgress<double>? progress = null)
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

        Directory.CreateDirectory(outputDirectory);

        var outputName =
            Path.GetFileNameWithoutExtension(inputIsoPath) +
            ".decrypted";

        var expectedOutputPath =
            Path.Combine(
                outputDirectory,
                outputName + ".iso");

        if (File.Exists(expectedOutputPath))
        {
            File.Delete(expectedOutputPath);
        }

        var inputSize =
            new FileInfo(inputIsoPath).Length;

        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--dk");
        startInfo.ArgumentList.Add(discKey);

        startInfo.ArgumentList.Add("--tc");
        startInfo.ArgumentList.Add(
            _threadCount.ToString());

        startInfo.ArgumentList.Add("--output-dir");
        startInfo.ArgumentList.Add(outputDirectory);

        startInfo.ArgumentList.Add("--output-name");
        startInfo.ArgumentList.Add(outputName);

        startInfo.ArgumentList.Add("--skip");
        startInfo.ArgumentList.Add(inputIsoPath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        // Drain both streams in the background so PS3Dec cannot block
        // because its output buffers filled up.
        var stdoutTask =
            process.StandardOutput.ReadToEndAsync();

        var stderrTask =
            process.StandardError.ReadToEndAsync();

        while (!process.HasExited)
        {
            if (File.Exists(expectedOutputPath) &&
                inputSize > 0)
            {
                try
                {
                    var outputSize =
                        new FileInfo(expectedOutputPath).Length;

                    var percentage =
                        outputSize * 100.0 / inputSize;

                    progress?.Report(
                        Math.Clamp(
                            percentage,
                            0,
                            99.9));
                }
                catch (IOException)
                {
                    // The file may briefly be unavailable while PS3Dec
                    // is writing to it. Just try again on the next poll.
                }
            }

            await Task.Delay(100);
        }

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
                $"ps3decrs exited with code {process.ExitCode}: {error.Trim()}");
        }

        if (!File.Exists(expectedOutputPath))
        {
            return (
                false,
                string.Empty,
                $"Decryption completed but expected output was not found: {expectedOutputPath}");
        }

        progress?.Report(100);

        return (
            true,
            expectedOutputPath,
            string.Empty);
    }
}