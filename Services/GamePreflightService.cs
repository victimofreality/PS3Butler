using PS3Butler.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class GamePreflightService
{
    private static readonly string[] SupportedExtensions =
    [
        ".iso",
        ".zip",
        ".7z",
        ".rar"
    ];

    private readonly KeyIndexService _keyIndex;
    private readonly ArchiveInspectionService _archiveInspectionService;
    private readonly DestinationInspectionService _destinationInspectionService;
    private readonly MetadataCacheService _metadataCacheService;
    private readonly string _isoDestinationDirectory;
    private readonly string _jbDestinationDirectory;
    private readonly PreflightReservationSet _reservations;
    private readonly Dictionary<
        string,
        CachedDestinationIndex> _destinationIndexes =
            new(
                StringComparer.OrdinalIgnoreCase);

    private sealed class CachedDestinationIndex
    {
        public required DestinationLibraryIndex Index { get; init; }

        public DateTime DirectoryLastWriteUtc { get; init; }
    }
    public GamePreflightService(
        KeyIndexService keyIndex,
        ArchiveInspectionService archiveInspectionService,
        DestinationInspectionService destinationInspectionService,
        MetadataCacheService metadataCacheService,
        string isoDestinationDirectory,
        string jbDestinationDirectory)
        : this(
            keyIndex,
            archiveInspectionService,
            destinationInspectionService,
            metadataCacheService,
            isoDestinationDirectory,
            jbDestinationDirectory,
            new PreflightReservationSet())
    {
    }

    public GamePreflightService(
        KeyIndexService keyIndex,
        ArchiveInspectionService archiveInspectionService,
        DestinationInspectionService destinationInspectionService,
        MetadataCacheService metadataCacheService,
        string isoDestinationDirectory,
        string jbDestinationDirectory,
        PreflightReservationSet reservations)
    {
        _keyIndex = keyIndex;
        _archiveInspectionService = archiveInspectionService;
        _destinationInspectionService = destinationInspectionService;
        _metadataCacheService = metadataCacheService;
        _isoDestinationDirectory = isoDestinationDirectory;
        _jbDestinationDirectory = jbDestinationDirectory;
        _reservations = reservations;
    }

    public async Task PreflightAsync(GameJob job)
    {
        job.KeyPath = null;
        job.DetectedIsoName = null;
        job.DiscKey = null;
        job.Details = string.Empty;

        if (job.SourceType == GameSourceType.Url &&
            string.IsNullOrWhiteSpace(job.LocalSourcePath))
        {
            job.Status = "Waiting";
            job.Details = "Waiting to download.";
            return;
        }

        var sourcePath = job.EffectiveSourcePath;

        if (!File.Exists(sourcePath))
        {
            job.Status = "Error";
            job.Details = "Source file does not exist.";
            return;
        }

        var extension = Path.GetExtension(sourcePath);

        if (!SupportedExtensions.Contains(
                extension,
                StringComparer.OrdinalIgnoreCase))
        {
            job.Status = "Error";
            job.Details = $"Unsupported file type: {extension}";
            return;
        }

        string gameName;

        if (extension.Equals(
                ".iso",
                StringComparison.OrdinalIgnoreCase))
        {
            job.DetectedIsoName =
                Path.GetFileName(sourcePath);

            gameName =
                Path.GetFileNameWithoutExtension(sourcePath);
        }
        else
        {
            job.Status = "Inspecting archive";

            var isoName =
                await _archiveInspectionService.FindIsoNameAsync(
                    sourcePath);

            if (isoName is null)
            {
                job.Status = "Error";
                job.Details =
                    "Archive does not contain exactly one detectable ISO.";
                return;
            }

            job.DetectedIsoName = isoName;

            gameName =
                Path.GetFileNameWithoutExtension(isoName);
        }

        var defaultDestinationDirectory =
            job.OutputFormat == OutputFormat.JbFolder
                ? _jbDestinationDirectory
                : _isoDestinationDirectory;

        var destinationDirectory =
            string.IsNullOrWhiteSpace(job.DestinationOverride)
                ? defaultDestinationDirectory
                : job.DestinationOverride;

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            job.Status = "Error";
            job.Details =
                job.OutputFormat == OutputFormat.JbFolder
                    ? "JB destination is not configured."
                    : "ISO destination is not configured.";
            return;
        }

        if (_reservations.IsGameReserved(
                job,
                destinationDirectory,
                gameName))
        {
            job.Status = "Already exists";
            job.Details =
                $"Another queued job already reserves: {gameName}";
            return;
        }

        if (job.OutputFormat == OutputFormat.EncryptedIsoWithKey)
        {
            var destinationIndex =
                await GetDestinationIndexAsync(
                    destinationDirectory);

            var existingEncryptedIso =
                destinationIndex.FindExistingIso(
                    gameName);

            if (existingEncryptedIso is not null)
            {
                var existingKeyPath =
                    Path.ChangeExtension(
                        existingEncryptedIso,
                        ".dkey");

                if (File.Exists(existingKeyPath))
                {
                    job.Status = "Already exists";
                    job.Details =
                        $"Destination already contains encrypted ISO + key: " +
                        $"{Path.GetFileName(existingEncryptedIso)}";
                }
                else
                {
                    job.Status = "Error";
                    job.Details =
                        $"Destination already contains ISO without matching .dkey: " +
                        $"{Path.GetFileName(existingEncryptedIso)}";
                }

                return;
            }

            var expectedIsoPath =
                Path.Combine(
                    destinationDirectory,
                    job.DetectedIsoName ??
                    $"{gameName}.iso");

            var expectedKeyPath =
                Path.ChangeExtension(
                    expectedIsoPath,
                    ".dkey");

            if (File.Exists(expectedKeyPath))
            {
                job.Status = "Error";
                job.Details =
                    $"Destination already contains .dkey without matching ISO: " +
                    $"{Path.GetFileName(expectedKeyPath)}";
                return;
            }
        }

        else
        {
            var destinationIndex =
                await GetDestinationIndexAsync(
                    destinationDirectory);

            var existingGame =
                destinationIndex.FindExistingGame(
                    gameName);

            if (existingGame is not null)
            {
                job.Status = "Already exists";
                job.Details =
                    $"Destination already contains: " +
                    $"{Path.GetFileName(existingGame)}";
                return;
            }
        }

        /*
         * If this job is being sent to the normal JB destination,
         * also check the ISO destination for an existing copy.
         *
         * Per-job overrides are treated as deliberate overrides and
         * therefore only inspect the overridden destination.
         */
        if (string.IsNullOrWhiteSpace(job.DestinationOverride) &&
            job.OutputFormat == OutputFormat.JbFolder &&
            !string.IsNullOrWhiteSpace(_isoDestinationDirectory))
        {
            var isoDestinationIndex =
                await GetDestinationIndexAsync(
                    _isoDestinationDirectory);

            var existingIso =
                isoDestinationIndex.FindExistingGame(
                    gameName);

            if (existingIso is not null)
            {
                job.Status = "Already exists";
                job.Details =
                    $"ISO destination already contains: " +
                    $"{Path.GetFileName(existingIso)}";
                return;
            }
        }

        var cachedMetadata =
            _metadataCacheService.FindByGameName(gameName);

        if (cachedMetadata is not null &&
            !string.IsNullOrWhiteSpace(cachedMetadata.TitleId))
        {
            if (_reservations.IsTitleIdReserved(
                    job,
                    destinationDirectory,
                    cachedMetadata.TitleId))
            {
                job.TitleId =
                    cachedMetadata.TitleId;

                job.Status =
                    "Already exists";

                job.Details =
                    $"Another queued job already reserves Title ID " +
                    $"{cachedMetadata.TitleId}.";

                return;
            }
            /*
             * For normal destinations, always check the JB library by
             * TITLE_ID so an ISO job does not create a duplicate of a
             * game already stored as a JB folder.
             */
            var jbDirectoryToCheck =
                string.IsNullOrWhiteSpace(job.DestinationOverride)
                    ? _jbDestinationDirectory
                    : destinationDirectory;

            if (!string.IsNullOrWhiteSpace(jbDirectoryToCheck))
            {
                var jbDestinationIndex =
                    await GetDestinationIndexAsync(
                        jbDirectoryToCheck);

                var existingJbGame =
                    jbDestinationIndex.FindJbGameByTitleId(
                        cachedMetadata.TitleId);

                if (existingJbGame is not null)
                {
                    job.TitleId = cachedMetadata.TitleId;
                    job.Status = "Already exists";
                    job.Details =
                        $"Destination already contains " +
                        $"{existingJbGame.Title} " +
                        $"({existingJbGame.TitleId}) as a JB folder.";
                    return;
                }
            }
        }

        var keyPath =
            _keyIndex.Find(gameName);

        if (keyPath is null)
        {
            job.Status = "Missing key";
            job.Details =
                $"No matching .dkey found for \"{gameName}\".";
            return;
        }

        job.KeyPath = keyPath;

        if (!KeyIndexService.TryReadKey(
                keyPath,
                out var discKey,
                out var keyError))
        {
            job.Status = "Invalid key";
            job.Details =
                $"{Path.GetFileName(keyPath)}: {keyError}";
            return;
        }

        job.DiscKey = discKey;

        _reservations.Reserve(
            job,
            destinationDirectory,
            gameName,
            cachedMetadata?.TitleId);

        job.Status = "Ready";

        if (extension.Equals(
                ".iso",
                StringComparison.OrdinalIgnoreCase))
        {
            job.Details =
                $"Key found: {Path.GetFileName(keyPath)}";
        }
        else
        {
            job.Details =
                $"ISO: {job.DetectedIsoName} | " +
                $"Key found: {Path.GetFileName(keyPath)}";
        }
    }
    private async Task<DestinationLibraryIndex> GetDestinationIndexAsync(
        string destinationDirectory)
    {
        var cacheKey =
            destinationDirectory.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);

        var directoryLastWriteUtc =
            await Task.Run(() =>
            {
                try
                {
                    return Directory.Exists(
                               destinationDirectory)
                        ? Directory.GetLastWriteTimeUtc(
                            destinationDirectory)
                        : DateTime.MinValue;
                }
                catch
                {
                    return DateTime.MinValue;
                }
            });

        if (_destinationIndexes.TryGetValue(
                cacheKey,
                out var cachedIndex) &&
            cachedIndex.DirectoryLastWriteUtc ==
                directoryLastWriteUtc)
        {
            return cachedIndex.Index;
        }

        /*
         * DestinationLibraryIndex construction enumerates the destination
         * library and may touch a network share. Keep that blocking filesystem
         * work off the Avalonia UI thread.
         */
        var index =
            await Task.Run(() =>
                _destinationInspectionService.CreateIndex(
                    destinationDirectory));

        _destinationIndexes[cacheKey] =
            new CachedDestinationIndex
            {
                Index =
                    index,

                DirectoryLastWriteUtc =
                    directoryLastWriteUtc
            };

        return index;
    }
}