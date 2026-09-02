using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class IsoMetadataService
{
    private readonly string _sevenZipPath;
    private readonly Ps3DiscSfbService _sfbService = new();

    public IsoMetadataService(string sevenZipPath)
    {
        _sevenZipPath = sevenZipPath;
    }

    public async Task<string?> ReadTitleIdAsync(
        string isoPath,
        string workDirectory)
    {
        if (!File.Exists(_sevenZipPath) ||
            !File.Exists(isoPath))
        {
            return null;
        }

        var tempDirectory = Path.Combine(
            workDirectory,
            "metadata",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDirectory);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _sevenZipPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("e");
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add($"-o{tempDirectory}");
            startInfo.ArgumentList.Add(isoPath);
            startInfo.ArgumentList.Add("PS3_DISC.SFB");

            using var process = new Process
            {
                StartInfo = startInfo
            };

            process.Start();

            var stdoutTask =
                process.StandardOutput.ReadToEndAsync();

            var stderrTask =
                process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            await stdoutTask;
            await stderrTask;

            if (process.ExitCode != 0)
            {
                return null;
            }

            var sfbPath =
                Path.Combine(
                    tempDirectory,
                    "PS3_DISC.SFB");

            return _sfbService.ReadTitleId(sfbPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(
                        tempDirectory,
                        recursive: true);
                }
            }
            catch
            {
                // Metadata cleanup failure is non-fatal.
            }
        }
    }
}