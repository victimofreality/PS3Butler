using PS3Butler.Models;
using System;
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

    public GamePreflightService(
        KeyIndexService keyIndex,
        ArchiveInspectionService archiveInspectionService)
    {
        _keyIndex = keyIndex;
        _archiveInspectionService = archiveInspectionService;
    }

    public async Task PreflightAsync(GameJob job)
    {
        job.KeyPath = null;
        job.DetectedIsoName = null;
        job.DiscKey = null;
        job.Details = string.Empty;

        if (!File.Exists(job.SourcePath))
        {
            job.Status = "Error";
            job.Details = "Source file does not exist.";
            return;
        }

        var extension = Path.GetExtension(job.SourcePath);

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
            job.DetectedIsoName = Path.GetFileName(job.SourcePath);
            gameName = Path.GetFileNameWithoutExtension(job.SourcePath);
        }
        else
        {
            job.Status = "Inspecting archive";

            var isoName =
                await _archiveInspectionService.FindIsoNameAsync(job.SourcePath);

            if (isoName is null)
            {
                job.Status = "Error";
                job.Details =
                    "Archive does not contain exactly one detectable ISO.";
                return;
            }

            job.DetectedIsoName = isoName;
            gameName = Path.GetFileNameWithoutExtension(isoName);
        }

        var keyPath = _keyIndex.Find(gameName);

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
                $"ISO: {job.DetectedIsoName} | Key found: {Path.GetFileName(keyPath)}";
        }
    }
}