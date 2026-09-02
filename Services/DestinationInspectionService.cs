using System;
using System.IO;
using System.Linq;

namespace PS3Butler.Services;

public class DestinationInspectionService
{
    public string? FindExistingGame(
        string destinationDirectory,
        string gameName)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return null;
        }

        if (!Directory.Exists(destinationDirectory))
        {
            return null;
        }

        var existingIso =
            FindExistingIso(
                destinationDirectory,
                gameName);

        if (existingIso is not null)
        {
            return existingIso;
        }

        var existingJbFolder =
            FindExistingJbFolder(
                destinationDirectory,
                gameName);

        return existingJbFolder;
    }

    private static string? FindExistingIso(
        string destinationDirectory,
        string gameName)
    {
        var candidates = Directory
            .EnumerateFiles(
                destinationDirectory,
                "*.iso",
                SearchOption.TopDirectoryOnly)
            .ToList();

        foreach (var path in candidates)
        {
            var fileName =
                Path.GetFileNameWithoutExtension(path);

            var normalizedName =
                NormalizeIsoName(fileName);

            if (string.Equals(
                    normalizedName,
                    gameName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return null;
    }

    private static string? FindExistingJbFolder(
        string destinationDirectory,
        string gameName)
    {
        foreach (var directory in Directory.EnumerateDirectories(
                     destinationDirectory,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var ps3GameDirectory =
                Path.Combine(directory, "PS3_GAME");

            if (!Directory.Exists(ps3GameDirectory))
            {
                continue;
            }

            var folderName =
                Path.GetFileName(directory);

            if (string.Equals(
                    folderName,
                    gameName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return directory;
            }
        }

        return null;
    }

    private static string NormalizeIsoName(string fileName)
    {
        const string decryptedSuffix = ".decrypted";

        if (fileName.EndsWith(
                decryptedSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            return fileName[..^decryptedSuffix.Length];
        }

        return fileName;
    }
}