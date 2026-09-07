using PS3Butler.Models;
using PS3Butler.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PS3Butler.Services.Pipeline;

public sealed class JobExecutionService
{
    private readonly MetadataCacheService _metadataCacheService;
    private readonly JobExecutionOptions _options;

    public JobExecutionService(
        MetadataCacheService metadataCacheService,
        JobExecutionOptions options)
    {
        _metadataCacheService = metadataCacheService;
        _options = options;
    }

    public async Task ProcessAsync(GameJob job)
    {
        var ps3DecService =
            new Ps3DecService(
                _options.RedrrxPs3DecPath,
                _options.Ps3DecThreadCount);

        var archiveExtractionService =
            new ArchiveExtractionService(
                _options.SevenZipPath);

        var jbExtractionService =
            new JbExtractionService(
                _options.SevenZipPath);

        var deliveryService =
            new DeliveryService();

        var folderDeliveryService =
            new FolderDeliveryService();

        var decryptedIsoMetadataService =
            new DecryptedIsoMetadataService(
                _options.SevenZipPath);

        if (job.Status != "Ready")
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(job.DiscKey))
        {
            job.Status = "Error";
            job.Details = "Disc key is missing.";
            return;
        }

        var defaultDestinationPath =
            job.OutputFormat == OutputFormat.JbFolder
                ? _options.JbDestinationPath
                : _options.IsoDestinationPath;

        var destinationPath =
            string.IsNullOrWhiteSpace(job.DestinationOverride)
                ? defaultDestinationPath
                : job.DestinationOverride;

        job.Progress = 0;
        job.CurrentStage = string.Empty;
        job.EtaText = string.Empty;
        job.TransferSpeedMbps = 0;

        string inputIsoPath;
        string? temporaryExtractionDirectory = null;
        string? temporaryJbDirectory = null;

        var sourcePath =
            job.EffectiveSourcePath;

        var extension =
            Path.GetExtension(sourcePath);

        if (extension.Equals(
                ".iso",
                StringComparison.OrdinalIgnoreCase))
        {
            inputIsoPath =
                sourcePath;
        }
        else
        {
            job.Status = "Extracting";
            job.CurrentStage = "Extracting archive";
            job.Progress = 0;
            job.Details =
                "Extracting ISO from archive...";

            temporaryExtractionDirectory =
                Path.Combine(
                    _options.WorkDirectory,
                    "archive",
                    Guid.NewGuid().ToString("N"));

            var extractionProgress =
                new Progress<double>(value =>
                {
                    job.Progress = value;
                });

            var extractionResult =
                await archiveExtractionService.ExtractIsoAsync(
                    sourcePath,
                    temporaryExtractionDirectory,
                    extractionProgress);

            if (!extractionResult.Success)
            {
                job.Status = "Error";
                job.CurrentStage = "Error";
                job.Details =
                    extractionResult.Error;
                return;
            }

            inputIsoPath =
                extractionResult.IsoPath;
        }
        if (job.OutputFormat ==
            OutputFormat.EncryptedIsoWithKey)
        {
            job.Status = "Copying";
            job.CurrentStage = "Copying encrypted ISO";
            job.Progress = 0;
            job.TransferSpeedMbps = 0;
            job.EtaText = string.Empty;
            job.Details =
                "Copying encrypted ISO and disc key to destination...";

            var finalIsoFileName =
                job.DetectedIsoName ??
                Path.GetFileName(inputIsoPath);

            var copyProgress =
                new Progress<TransferProgress>(value =>
                {
                    job.Progress =
                        value.Percentage;

                    job.TransferSpeedMbps =
                        value.MegabytesPerSecond;

                    if (value.EstimatedRemaining.HasValue)
                    {
                        var eta =
                            value.EstimatedRemaining.Value;

                        job.EtaText =
                            $"Copying — " +
                            $"{value.MegabytesPerSecond:F1} MB/s — " +
                            $"{eta:hh\\:mm\\:ss} remaining";
                    }
                    else
                    {
                        job.EtaText =
                            $"Copying — " +
                            $"{value.MegabytesPerSecond:F1} MB/s";
                    }
                });

            var deliveryResult =
                await deliveryService.CopyAsync(
                    inputIsoPath,
                    destinationPath,
                    copyProgress,
                    finalIsoFileName);

            if (!deliveryResult.Success)
            {
                job.Status = "Error";
                job.CurrentStage = "Error";
                job.Details =
                    deliveryResult.Error;
                return;
            }

            var destinationKeyPath =
                Path.ChangeExtension(
                    deliveryResult.DestinationPath,
                    ".dkey");

            await File.WriteAllTextAsync(
                destinationKeyPath,
                job.DiscKey);

            job.OutputPath =
                deliveryResult.DestinationPath;

            try
            {
                if (temporaryExtractionDirectory is not null &&
                    Directory.Exists(
                        temporaryExtractionDirectory))
                {
                    Directory.Delete(
                        temporaryExtractionDirectory,
                        recursive: true);
                }

                if (job.SourceType == GameSourceType.Url &&
                    !string.IsNullOrWhiteSpace(job.LocalSourcePath) &&
                    File.Exists(job.LocalSourcePath))
                {
                    var downloadDirectory =
                        Path.GetDirectoryName(job.LocalSourcePath);

                    File.Delete(job.LocalSourcePath);

                    if (!string.IsNullOrWhiteSpace(downloadDirectory) &&
                        Directory.Exists(downloadDirectory) &&
                        !Directory.EnumerateFileSystemEntries(downloadDirectory).Any())
                    {
                        Directory.Delete(downloadDirectory);
                    }

                    job.LocalSourcePath = null;
                }
            }
            catch (Exception ex)
            {
                job.Status = "Complete";
                job.CurrentStage = "Complete";
                job.Progress = 100;
                job.EtaText = string.Empty;
                job.Details =
                    $"Encrypted ISO and key copied successfully, but temporary cleanup failed: {ex.Message}";
                return;
            }
            job.Status = "Complete";
            job.CurrentStage = "Complete";
            job.Progress = 100;
            job.EtaText = string.Empty;
            job.Details =
                $"Encrypted ISO and key copied to: {job.OutputPath}";

            return;
        }

        job.Status = "Decrypting";
        job.CurrentStage = "Decrypting";
        job.Progress = 0;
        job.Details =
            "Decrypting ISO with ps3decrs...";

        var decryptProgress =
            new Progress<double>(value =>
            {
                job.Progress = value;
            });

        var decryptResult =
            await ps3DecService.DecryptAsync(
                inputIsoPath,
                job.DiscKey,
                _options.WorkDirectory,
                decryptProgress);

        if (!decryptResult.Success)
        {
            job.Status = "Error";
            job.CurrentStage = "Error";
            job.Details =
                decryptResult.Error;
            return;
        }

        var metadata =
            await decryptedIsoMetadataService.ReadMetadataAsync(
                decryptResult.OutputPath,
                _options.WorkDirectory);

        if (metadata is not null &&
            !string.IsNullOrWhiteSpace(metadata.TitleId))
        {
            var gameName =
                Path.GetFileNameWithoutExtension(
                    job.DetectedIsoName ??
                    sourcePath);

            _metadataCacheService.AddOrUpdate(
                gameName,
                metadata.TitleId,
                metadata.Title);

            job.TitleId =
                metadata.TitleId;
        }

        if (job.OutputFormat ==
            OutputFormat.DecryptedIso)
        {
            job.Status = "Copying";
            job.CurrentStage = "Copying ISO";
            job.Progress = 0;
            job.TransferSpeedMbps = 0;
            job.EtaText = string.Empty;
            job.Details =
                "Copying decrypted ISO to destination...";

            var copyProgress =
                new Progress<TransferProgress>(value =>
                {
                    job.Progress =
                        value.Percentage;

                    job.TransferSpeedMbps =
                        value.MegabytesPerSecond;

                    if (value.EstimatedRemaining.HasValue)
                    {
                        var eta =
                            value.EstimatedRemaining.Value;

                        job.EtaText =
                            $"Copying — " +
                            $"{value.MegabytesPerSecond:F1} MB/s — " +
                            $"{eta:hh\\:mm\\:ss} remaining";
                    }
                    else
                    {
                        job.EtaText =
                            $"Copying — " +
                            $"{value.MegabytesPerSecond:F1} MB/s";
                    }
                });

            var finalIsoFileName =
                job.DetectedIsoName ??
                Path.GetFileName(sourcePath);

            var deliveryResult =
                await deliveryService.CopyAsync(
                    decryptResult.OutputPath,
                    destinationPath,
                    copyProgress,
                    finalIsoFileName);

            if (!deliveryResult.Success)
            {
                job.Status = "Error";
                job.CurrentStage = "Error";
                job.Details =
                    deliveryResult.Error;
                return;
            }

            job.OutputPath =
                deliveryResult.DestinationPath;
        }
        else if (job.OutputFormat ==
                 OutputFormat.JbFolder)
        {
            job.Status = "Extracting";
            job.CurrentStage = "Creating JB folder";
            job.Progress = 0;
            job.Details =
                "Extracting decrypted ISO to JB folder...";

            string gameFolderName;

            if (metadata is not null &&
                !string.IsNullOrWhiteSpace(metadata.TitleId) &&
                !string.IsNullOrWhiteSpace(metadata.Title))
            {
                gameFolderName =
                    MakeSafeFolderName(
                        $"{metadata.Title} [{metadata.TitleId}]");
            }
            else
            {
                gameFolderName =
                    MakeSafeFolderName(
                        Path.GetFileNameWithoutExtension(
                            job.DetectedIsoName ??
                            sourcePath));
            }
            temporaryJbDirectory =
                Path.Combine(
                    _options.WorkDirectory,
                    "jb",
                    Guid.NewGuid().ToString("N"));

            var jbProgress =
                new Progress<double>(value =>
                {
                    job.Progress = value;
                });

            var jbExtractionResult =
                await jbExtractionService.ExtractAsync(
                    decryptResult.OutputPath,
                    temporaryJbDirectory,
                    jbProgress);

            if (!jbExtractionResult.Success)
            {
                job.Status = "Error";
                job.CurrentStage = "Error";
                job.Details =
                    jbExtractionResult.Error;
                return;
            }

            if (_options.RemovePs3Update)
            {
                var ps3UpdateDirectory =
                    Path.Combine(
                        temporaryJbDirectory,
                        "PS3_UPDATE");

                if (Directory.Exists(ps3UpdateDirectory))
                {
                    Directory.Delete(
                        ps3UpdateDirectory,
                        recursive: true);
                }
            }
            var finalJbDestination =
                Path.Combine(
                    destinationPath,
                    gameFolderName);

            job.Status = "Copying";
            job.CurrentStage = "Copying JB folder";
            job.Progress = 0;
            job.TransferSpeedMbps = 0;
            job.EtaText = string.Empty;
            job.Details =
                "Copying JB folder to destination...";

            var folderCopyProgress =
                new Progress<TransferProgress>(value =>
                {
                    job.Progress =
                        value.Percentage;

                    job.TransferSpeedMbps =
                        value.MegabytesPerSecond;

                    if (value.EstimatedRemaining.HasValue)
                    {
                        var eta =
                            value.EstimatedRemaining.Value;

                        job.EtaText =
                            $"Copying — " +
                            $"{value.MegabytesPerSecond:F1} MB/s — " +
                            $"{eta:hh\\:mm\\:ss} remaining";
                    }
                    else
                    {
                        job.EtaText =
                            $"Copying — " +
                            $"{value.MegabytesPerSecond:F1} MB/s";
                    }
                });

            var folderDeliveryResult =
                await folderDeliveryService.CopyAsync(
                    temporaryJbDirectory,
                    finalJbDestination,
                    folderCopyProgress);

            if (!folderDeliveryResult.Success)
            {
                job.Status = "Error";
                job.CurrentStage = "Error";
                job.Details =
                    folderDeliveryResult.Error;
                return;
            }

            job.OutputPath =
                folderDeliveryResult.DestinationPath;
        }

        try
        {
            if (File.Exists(
                    decryptResult.OutputPath))
            {
                File.Delete(
                    decryptResult.OutputPath);
            }

            if (temporaryExtractionDirectory is not null &&
                Directory.Exists(
                    temporaryExtractionDirectory))
            {
                Directory.Delete(
                    temporaryExtractionDirectory,
                    recursive: true);
            }

            if (temporaryJbDirectory is not null &&
                Directory.Exists(
                    temporaryJbDirectory))
            {
                Directory.Delete(
                    temporaryJbDirectory,
                    recursive: true);
            }
            if (job.SourceType == GameSourceType.Url &&
                !string.IsNullOrWhiteSpace(job.LocalSourcePath) &&
                File.Exists(job.LocalSourcePath))
            {
                var downloadDirectory =
                    Path.GetDirectoryName(job.LocalSourcePath);

                File.Delete(job.LocalSourcePath);

                if (!string.IsNullOrWhiteSpace(downloadDirectory) &&
                    Directory.Exists(downloadDirectory) &&
                    !Directory.EnumerateFileSystemEntries(downloadDirectory).Any())
                {
                    Directory.Delete(downloadDirectory);
                }

                job.LocalSourcePath = null;
            }
        }
        catch (Exception ex)
        {
            job.Status = "Complete";
            job.CurrentStage = "Complete";
            job.Progress = 100;
            job.EtaText = string.Empty;
            job.Details =
                $"Output completed successfully, but temporary cleanup failed: {ex.Message}";
            return;
        }

        job.Status = "Complete";
        job.CurrentStage = "Complete";
        job.Progress = 100;
        job.EtaText = string.Empty;

        if (job.OutputFormat ==
            OutputFormat.DecryptedIso)
        {
            job.Details =
                $"Decrypted ISO copied to: {job.OutputPath}";
        }
        else
        {
            job.Details =
                $"JB folder copied to: {job.OutputPath}";
        }

    }

    private static string MakeSafeFolderName(string name)
    {
        name = name
            .Replace("™", string.Empty)
            .Replace("®", string.Empty)
            .Replace("©", string.Empty)
            .Replace(":", string.Empty);

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidCharacter, '_');
        }

        while (name.Contains("  "))
        {
            name = name.Replace("  ", " ");
        }

        return name.Trim();
    }
}
