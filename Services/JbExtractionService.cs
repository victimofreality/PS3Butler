using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class JbExtractionService
{
    private static readonly Regex ProgressRegex =
        new(@"(?<percent>\d{1,3})%",
            RegexOptions.Compiled);

    private readonly string _sevenZipPath;

    public JbExtractionService(string sevenZipPath)
    {
        _sevenZipPath = sevenZipPath;
    }

    public async Task<(bool Success, string Error)> ExtractAsync(
        string decryptedIsoPath,
        string outputDirectory,
        IProgress<double>? progress = null)
    {
        if (!File.Exists(_sevenZipPath))
        {
            return (false, "7-Zip executable not found.");
        }

        if (!File.Exists(decryptedIsoPath))
        {
            return (false, "Decrypted ISO not found.");
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
        startInfo.ArgumentList.Add(decryptedIsoPath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        process.Start();

        var stderrTask =
            process.StandardError.ReadToEndAsync();

        var buffer = new char[256];
        var pendingText = string.Empty;

        while (true)
        {
            var charsRead =
                await process.StandardOutput.ReadAsync(
                    buffer.AsMemory(0, buffer.Length));

            if (charsRead == 0)
            {
                break;
            }

            pendingText +=
                new string(buffer, 0, charsRead);

            foreach (Match match in ProgressRegex.Matches(pendingText))
            {
                if (double.TryParse(
                        match.Groups["percent"].Value,
                        out var percentage))
                {
                    progress?.Report(
                        Math.Clamp(percentage, 0, 100));
                }
            }

            if (pendingText.Length > 1024)
            {
                pendingText =
                    pendingText[^512..];
            }
        }

        await process.WaitForExitAsync();

        var stderr = await stderrTask;

        var ps3GameDirectory =
            Path.Combine(
                outputDirectory,
                "PS3_GAME");

        if (!Directory.Exists(ps3GameDirectory))
        {
            return (
                false,
                $"Extraction finished but PS3_GAME was not found. 7-Zip exit code: {process.ExitCode}. {stderr.Trim()}");
        }

        progress?.Report(100);

        return (true, string.Empty);
    }
}