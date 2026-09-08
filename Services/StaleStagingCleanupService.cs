using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PS3Butler.Services;

public static partial class StaleStagingCleanupService
{
    private static readonly TimeSpan MinimumAge =
        TimeSpan.FromHours(24);

    public static int Cleanup(
        params string?[] destinationDirectories)
    {
        var removedCount = 0;

        foreach (var destinationDirectory in
                 destinationDirectories
                     .Where(path =>
                         !string.IsNullOrWhiteSpace(path))
                     .Distinct(
                         StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(
                    destinationDirectory))
            {
                continue;
            }

            removedCount +=
                CleanupDirectory(
                    destinationDirectory!);
        }

        return removedCount;
    }

    private static int CleanupDirectory(
        string destinationDirectory)
    {
        var removedCount = 0;
        var cutoffUtc =
            DateTime.UtcNow - MinimumAge;

        IEnumerable<string> entries;

        try
        {
            entries =
                Directory.EnumerateFileSystemEntries(
                    destinationDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .ToList();
        }
        catch
        {
            return 0;
        }

        foreach (var entry in entries)
        {
            var name =
                Path.GetFileName(entry);

            if (!IsButlerStagingArtifact(name))
            {
                continue;
            }

            try
            {
                var lastWriteUtc =
                    File.Exists(entry)
                        ? File.GetLastWriteTimeUtc(entry)
                        : GetNewestWriteTimeUtc(entry);

                /*
                 * Never touch a recent staging artifact. Another Butler
                 * instance may still be using the same destination.
                 */
                if (lastWriteUtc > cutoffUtc)
                {
                    continue;
                }

                if (File.Exists(entry))
                {
                    File.Delete(entry);
                    removedCount++;
                }
                else if (Directory.Exists(entry))
                {
                    Directory.Delete(
                        entry,
                        recursive: true);

                    removedCount++;
                }
            }
            catch
            {
                // Best-effort startup cleanup only.
            }
        }

        return removedCount;
    }

    private static DateTime GetNewestWriteTimeUtc(
        string directory)
    {
        var newest =
            Directory.GetLastWriteTimeUtc(
                directory);

        try
        {
            foreach (var file in
                     Directory.EnumerateFiles(
                         directory,
                         "*",
                         SearchOption.AllDirectories))
            {
                var lastWrite =
                    File.GetLastWriteTimeUtc(
                        file);

                if (lastWrite > newest)
                {
                    newest = lastWrite;
                }
            }
        }
        catch
        {
            /*
             * If the tree cannot be inspected completely, return "now" so
             * cleanup errs on the side of leaving it alone.
             */
            return DateTime.UtcNow;
        }

        return newest;
    }

    private static bool IsButlerStagingArtifact(
        string name)
    {
        return PartialArtifactRegex().IsMatch(name) ||
               EncryptedIsoArtifactRegex().IsMatch(name) ||
               EncryptedKeyArtifactRegex().IsMatch(name);
    }

    /*
     * DeliveryService / FolderDeliveryService:
     *   <final-name>.ps3butler-<guid>.partial
     */
    [GeneratedRegex(
        @".+\.ps3butler-[0-9a-fA-F]{32}\.partial$",
        RegexOptions.CultureInvariant)]
    private static partial Regex PartialArtifactRegex();

    /*
     * Transactional encrypted ISO + key staging:
     *   .ps3butler-<guid>.iso
     *   .ps3butler-<guid>.dkey
     */
    [GeneratedRegex(
        @"^\.ps3butler-[0-9a-fA-F]{32}\.iso$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EncryptedIsoArtifactRegex();

    [GeneratedRegex(
        @"^\.ps3butler-[0-9a-fA-F]{32}\.dkey$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EncryptedKeyArtifactRegex();
}
