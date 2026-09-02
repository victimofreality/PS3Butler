using PS3Butler.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class DecryptedIsoMetadataService
{
    private readonly string _sevenZipPath;
    private readonly ParamSfoService _paramSfoService = new();

    public DecryptedIsoMetadataService(string sevenZipPath)
    {
        _sevenZipPath = sevenZipPath;
    }

    public async Task<Ps3GameMetadata?> ReadMetadataAsync(
        string decryptedIsoPath,
        string workDirectory)
    {
        if (!File.Exists(_sevenZipPath))
        {
            return null;
        }

        if (!File.Exists(decryptedIsoPath))
        {
            return null;
        }

        var tempDirectory =
            Path.Combine(
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
            startInfo.ArgumentList.Add(decryptedIsoPath);
            startInfo.ArgumentList.Add(@"PS3_GAME\PARAM.SFO");

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

            var paramSfoPath =
                Path.Combine(
                    tempDirectory,
                    "PARAM.SFO");

            if (!File.Exists(paramSfoPath))
            {
                return null;
            }

            return _paramSfoService.Read(paramSfoPath);
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
                // Metadata temp cleanup failure is non-fatal.
            }
        }
    }
}