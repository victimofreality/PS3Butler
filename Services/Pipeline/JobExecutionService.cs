using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Services.Storage;
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
    private readonly IDiskBudgetService _diskBudgetService;

    private const long WorkSafetyMarginBytes =
        2L * 1024 * 1024 * 1024;
    private const long DestinationSafetyMarginBytes =
        2L * 1024 * 1024 * 1024;

    public JobExecutionService(
        MetadataCacheService metadataCacheService,
        JobExecutionOptions options)
        : this(
            metadataCacheService,
            options,
            new DiskBudgetService())
    {
    }

    public JobExecutionService(
        MetadataCacheService metadataCacheService,
        JobExecutionOptions options,
        IDiskBudgetService diskBudgetService)
    {
        _metadataCacheService =
            metadataCacheService;

        _options =
            options;

        _diskBudgetService =
            diskBudgetService;
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

        if (job.State != JobState.Ready)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(job.DiscKey))
        {
            job.State = JobState.Error;
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
        job.Stage = JobStage.None;
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
                job.Stage = JobStage.ExtractingArchive;
                job.State = JobState.Running;
                job.Progress = 0;
                job.Details =
                    "Extracting ISO from archive...";

                var archiveSize =
                    new FileInfo(sourcePath).Length;

                /*
                 * Until archive inspection reports the ISO's declared
                 * uncompressed size, use a conservative estimate.
                 *
                 * PS3 disc images often compress poorly, but 2x the archive
                 * size gives us useful protection without pretending we know
                 * the exact extracted size yet.
                 */
                var estimatedExtractedIsoSize =
                    archiveSize > long.MaxValue / 2
                        ? long.MaxValue
                        : archiveSize * 2;

                var extractionDiskBudget =
                    await _diskBudgetService.CheckAsync(
                        _options.WorkDirectory,
                        estimatedExtractedIsoSize,
                        WorkSafetyMarginBytes,
                        cancellationToken);

                if (!extractionDiskBudget.IsSufficient)
                {
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
                    job.Details =
                        $"Not enough work-disk space to extract archive. " +
                        extractionDiskBudget.Message;
                    return;
                }

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
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
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
                var encryptedDestinationSize =
                    new FileInfo(inputIsoPath).Length;

                var encryptedDestinationBudget =
                    await _diskBudgetService.CheckAsync(
                        destinationPath,
                        encryptedDestinationSize,
                        DestinationSafetyMarginBytes,
                        cancellationToken);

                if (!encryptedDestinationBudget.IsSufficient)
                {
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
                    job.Details =
                        $"Not enough destination space for encrypted ISO. " +
                        encryptedDestinationBudget.Message;
                    return;
                }

                job.Stage = JobStage.Copying;
                job.State = JobState.Running;
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
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
                    job.Details =
                        $"Destination ISO already exists: {finalIsoPath}";
                    return;
                }

                if (File.Exists(finalKeyPath))
                {
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
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
                        job.State = JobState.Error;
                        job.Stage = JobStage.Error;
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
                        job.State = JobState.Error;
                        job.Stage = JobStage.Error;
                        job.Details =
                            "Staged encrypted ISO was not found.";
                        return;
                    }

                    if (!File.Exists(stagedKeyPath))
                    {
                        job.State = JobState.Error;
                        job.Stage = JobStage.Error;
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
                        job.State = JobState.Error;
                        job.Stage = JobStage.Error;
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
                        job.State = JobState.Error;
                        job.Stage = JobStage.Error;
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
                    job.State = JobState.Error;
                    job.Stage = JobStage.Cancelled;
                    job.Details =
                        "Encrypted ISO delivery cancelled.";
                    return;
                }
                catch (Exception ex)
                {
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
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

                job.State = JobState.Complete;
                job.Stage = JobStage.Complete;
                job.Progress = 100;
                job.EtaText = string.Empty;
                job.Details =
                    $"Encrypted ISO and key copied to: {job.OutputPath}";

                return;
            }
            var encryptedIsoSize =
                new FileInfo(inputIsoPath).Length;

            var decryptionDiskBudget =
                await _diskBudgetService.CheckAsync(
                    _options.WorkDirectory,
                    encryptedIsoSize,
                    WorkSafetyMarginBytes,
                    cancellationToken);

            if (!decryptionDiskBudget.IsSufficient)
            {
                job.State = JobState.Error;
                job.Stage = JobStage.Error;
                job.Details =
                    $"Not enough work-disk space to decrypt ISO. " +
                    decryptionDiskBudget.Message;
                return;
            }

            job.Stage = JobStage.Decrypting;
            job.State = JobState.Running;
            job.Progress = 0;
            job.Details =
                "Decrypting ISO with Redrrx PS3Dec...";

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
                job.State = JobState.Error;
                job.Stage = JobStage.Error;
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
                job.Stage = JobStage.Copying;
                job.State = JobState.Running;
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

                var decryptedIsoDestinationSize =
                    new FileInfo(
                        decryptResult.OutputPath).Length;

                var decryptedIsoDestinationBudget =
                    await _diskBudgetService.CheckAsync(
                        destinationPath,
                        decryptedIsoDestinationSize,
                        DestinationSafetyMarginBytes,
                        cancellationToken);

                if (!decryptedIsoDestinationBudget.IsSufficient)
                {
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
                    job.Details =
                        $"Not enough destination space for decrypted ISO. " +
                        decryptedIsoDestinationBudget.Message;
                    return;
                }

                var deliveryResult =
                    await deliveryService.CopyAsync(
                        decryptResult.OutputPath,
                        destinationPath,
                        copyProgress,
                        finalIsoFileName,
                        cancellationToken);

                if (!deliveryResult.Success)
                {
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
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
                var decryptedIsoSize =
                    new FileInfo(
                        decryptResult.OutputPath).Length;

                var jbWorkspaceBudget =
                    await _diskBudgetService.CheckAsync(
                        _options.WorkDirectory,
                        decryptedIsoSize,
                        WorkSafetyMarginBytes,
                        cancellationToken);

                if (!jbWorkspaceBudget.IsSufficient)
                {
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
                    job.Details =
                        $"Not enough work-disk space to create JB folder. " +
                        jbWorkspaceBudget.Message;
                    return;
                }

                job.Stage = JobStage.CreatingJbFolder;
                job.State = JobState.Running;
                // Keep the existing status label while using the specific JB stage.
                job.Status = "Extracting";
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
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
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
                var jbDestinationSize =
                    GetDirectorySize(
                        temporaryJbDirectory);

                var jbDestinationBudget =
                    await _diskBudgetService.CheckAsync(
                        destinationPath,
                        jbDestinationSize,
                        DestinationSafetyMarginBytes,
                        cancellationToken);

                if (!jbDestinationBudget.IsSufficient)
                {
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
                    job.Details =
                        $"Not enough destination space for JB folder. " +
                        jbDestinationBudget.Message;
                    return;
                }

                var finalJbDestination =
                    Path.Combine(
                        destinationPath,
                        gameFolderName);

                job.Stage = JobStage.Copying;
                job.State = JobState.Running;
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
                    job.State = JobState.Error;
                    job.Stage = JobStage.Error;
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
            }
            catch (Exception ex)
            {
                job.State = JobState.Complete;
                job.Stage = JobStage.Complete;
                job.Progress = 100;
                job.EtaText = string.Empty;
                job.Details =
                    $"Output completed successfully, but temporary cleanup failed: {ex.Message}";
                return;
            }

            job.State = JobState.Complete;
            job.Stage = JobStage.Complete;
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

            if (job.SourceType ==
                    GameSourceType.Url &&
                !string.IsNullOrWhiteSpace(
                    job.LocalSourcePath))
            {
                TemporaryDownloadCleanupService.CleanupFile(
                    job.LocalSourcePath,
                    _options.WorkDirectory);

                job.LocalSourcePath =
                    null;
            }
        }

    }

    private static long GetDirectorySize(
        string directoryPath)
    {
        long totalBytes = 0;

        foreach (var filePath in
                 Directory.EnumerateFiles(
                     directoryPath,
                     "*",
                     SearchOption.AllDirectories))
        {
            var fileLength =
                new FileInfo(filePath).Length;

            if (totalBytes >
                long.MaxValue - fileLength)
            {
                return long.MaxValue;
            }

            totalBytes +=
                fileLength;
        }

        return totalBytes;
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
