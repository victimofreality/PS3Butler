using PS3Butler.Models;
using PS3Butler.Services.Execution;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class DecryptedIsoMetadataService
{
    private readonly string _sevenZipPath;
    private readonly ParamSfoService _paramSfoService;
    private readonly IProcessRunner _processRunner;

    public DecryptedIsoMetadataService(
        string sevenZipPath)
        : this(
            sevenZipPath,
            new ProcessRunner(),
            new ParamSfoService())
    {
    }

    public DecryptedIsoMetadataService(
        string sevenZipPath,
        IProcessRunner processRunner,
        ParamSfoService paramSfoService)
    {
        _sevenZipPath = sevenZipPath;
        _processRunner = processRunner;
        _paramSfoService = paramSfoService;
    }

    public async Task<Ps3GameMetadata?> ReadMetadataAsync(
        string decryptedIsoPath,
        string workDirectory,
        CancellationToken cancellationToken = default)
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

        Directory.CreateDirectory(
            tempDirectory);

        try
        {
            var request =
                new ProcessRunRequest
                {
                    FileName =
                        _sevenZipPath,

                    Arguments =
                        $"e -y " +
                        $"-o\"{tempDirectory}\" " +
                        $"\"{decryptedIsoPath}\" " +
                        "\"PS3_GAME\\PARAM.SFO\"",

                    Timeout =
                        TimeSpan.FromSeconds(30)
                };

            var result =
                await _processRunner.RunAsync(
                    request,
                    cancellationToken);

            if (result.WasCancelled ||
                result.TimedOut)
            {
                return null;
            }

            /*
             * Decrypted PS3 ISOs can make 7-Zip report a
             * Headers Error even when PARAM.SFO was actually
             * extracted successfully.
             *
             * For metadata extraction, the authoritative check
             * is whether PARAM.SFO exists and parses.
             */
            var paramSfoPath =
                Path.Combine(
                    tempDirectory,
                    "PARAM.SFO");

            if (!File.Exists(paramSfoPath))
            {
                return null;
            }

            return _paramSfoService.Read(
                paramSfoPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(
                        tempDirectory))
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