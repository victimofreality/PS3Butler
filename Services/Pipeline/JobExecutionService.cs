using PS3Butler.Models;
using PS3Butler.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading;
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

    public async Task ProcessAsync(
     GameJob job,
     CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
        string? decryptedIsoPath = null;

        try
        {
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
                    extractionProgress,
                    cancellationToken);

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

                var finalIsoPath =
                    Path.Combine(
                        destinationPath,
                        finalIsoFileName);

                var finalKeyPath =
                    Path.ChangeExtension(
                        finalIsoPath,
                        ".dkey");

                if (File.Exists(finalIsoPath))
                {
                    job.Status = "Error";
                    job.CurrentStage = "Error";
                    job.Details =
                        $"Destination ISO already exists: {finalIsoPath}";
                    return;
                }

                if (File.Exists(finalKeyPath))
                {
                    job.Status = "Error";
                    job.CurrentStage = "Error";
                    job.Details =
                        $"Destination key already exists: {finalKeyPath}";
                    return;
                }

                var transactionId =
                    Guid.NewGuid().ToString("N");

                var stagedIsoFileName =
                    $".ps3butler-{transactionId}.iso";

                var stagedIsoPath =
                    Path.Combine(
                        destinationPath,
                        stagedIsoFileName);

                var stagedKeyPath =
                    Path.Combine(
                        destinationPath,
                        $".ps3butler-{transactionId}.dkey");

                var isoPublished =
                    false;

                var keyPublished =
                    false;

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

                try
                {
                    var deliveryResult =
                        await deliveryService.CopyAsync(
                            inputIsoPath,
                            destinationPath,
                            copyProgress,
                            stagedIsoFileName,
                            cancellationToken);

                    if (!deliveryResult.Success)
                    {
                        job.Status = "Error";
                        job.CurrentStage = "Error";
                        job.Details =
                            deliveryResult.Error;
                        return;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    await File.WriteAllTextAsync(
                        stagedKeyPath,
                        job.DiscKey,
                        cancellationToken);

                    if (!File.Exists(stagedIsoPath))
                    {
                        job.Status = "Error";
                        job.CurrentStage = "Error";
                        job.Details =
                            "Staged encrypted ISO was not found.";
                        return;
                    }

                    if (!File.Exists(stagedKeyPath))
                    {
                        job.Status = "Error";
                        job.CurrentStage = "Error";
                        job.Details =
                            "Staged disc key was not found.";
                        return;
                    }

                    var stagedIsoInfo =
                        new FileInfo(stagedIsoPath);

                    var sourceIsoInfo =
                        new FileInfo(inputIsoPath);

                    if (stagedIsoInfo.Length !=
                        sourceIsoInfo.Length)
                    {
                        job.Status = "Error";
                        job.CurrentStage = "Error";
                        job.Details =
                            "Staged encrypted ISO size does not match source.";
                        return;
                    }

                    var stagedKeyText =
                        await File.ReadAllTextAsync(
                            stagedKeyPath,
                            cancellationToken);

                    if (!string.Equals(
                            stagedKeyText.Trim(),
                            job.DiscKey.Trim(),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        job.Status = "Error";
                        job.CurrentStage = "Error";
                        job.Details =
                            "Staged disc key does not match expected key.";
                        return;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    /*
                     * Publish the pair.
                     *
                     * Both staged files are already complete and live on the
                     * destination filesystem, so these are rename operations.
                     *
                     * If publishing the second file fails, the catch block
                     * rolls the first one back.
                     */
                    File.Move(
                        stagedIsoPath,
                        finalIsoPath,
                        overwrite: false);

                    isoPublished =
                        true;

                    File.Move(
                        stagedKeyPath,
                        finalKeyPath,
                        overwrite: false);

                    keyPublished =
                        true;

                    job.OutputPath =
                        finalIsoPath;
                }
                catch (OperationCanceledException)
                {
                    job.Status = "Error";
                    job.CurrentStage = "Error";
                    job.Details =
                        "Encrypted ISO delivery cancelled.";
                    return;
                }
                catch (Exception ex)
                {
                    job.Status = "Error";
                    job.CurrentStage = "Error";
                    job.Details =
                        $"Encrypted ISO/key delivery failed: {ex.Message}";
                    return;
                }
                finally
                {
                    /*
                     * If only one member of the pair was published,
                     * remove it again so the destination never contains
                     * a half-published ISO/key pair.
                     */
                    if (isoPublished &&
                        !keyPublished)
                    {
                        TryDeleteFile(
                            finalIsoPath);
                    }

                    if (keyPublished &&
                        !isoPublished)
                    {
                        TryDeleteFile(
                            finalKeyPath);
                    }

                    TryDeleteFile(
                        stagedIsoPath);

                    TryDeleteFile(
                        stagedKeyPath);
                }

                try
                {
                    if (job.SourceType == GameSourceType.Url &&
                        !string.IsNullOrWhiteSpace(job.LocalSourcePath) &&
                        File.Exists(job.LocalSourcePath))
                    {
                        var downloadDirectory =
                            Path.GetDirectoryName(
                                job.LocalSourcePath);

                        File.Delete(
                            job.LocalSourcePath);

                        if (!string.IsNullOrWhiteSpace(
                                downloadDirectory) &&
                            Directory.Exists(
                                downloadDirectory) &&
                            !Directory
                                .EnumerateFileSystemEntries(
                                    downloadDirectory)
                                .Any())
                        {
                            Directory.Delete(
                                downloadDirectory);
                        }

                        job.LocalSourcePath =
                            null;
                    }
                }
                catch (Exception ex)
                {
                    job.Status = "Complete";
                    job.CurrentStage = "Complete";
                    job.Progress = 100;
                    job.EtaText = string.Empty;
                    job.Details =
                        $"Encrypted ISO and key copied successfully, " +
                        $"but temporary cleanup failed: {ex.Message}";
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
                decryptProgress,
                cancellationToken);

        if (!decryptResult.Success)
        {
            job.Status = "Error";
            job.CurrentStage = "Error";
            job.Details =
                decryptResult.Error;
            return;
        }

            decryptedIsoPath =
                decryptResult.OutputPath;

            var metadata =
            await decryptedIsoMetadataService.ReadMetadataAsync(
                decryptResult.OutputPath,
                _options.WorkDirectory,
                cancellationToken);

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
                    finalIsoFileName,
                    cancellationToken);

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
                    jbProgress,
                    cancellationToken);

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
                    folderCopyProgress,
                    cancellationToken);

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
        finally
        {
            TryDeleteFile(
                decryptedIsoPath);

            TryDeleteDirectory(
                temporaryExtractionDirectory);

            TryDeleteDirectory(
                temporaryJbDirectory);
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

    private static void TryDeleteFile(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort temporary cleanup only.
        }
    }

    private static void TryDeleteDirectory(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(
                    path,
                    recursive: true);
            }
        }
        catch
        {
            // Best-effort temporary cleanup only.
        }
    }
}
