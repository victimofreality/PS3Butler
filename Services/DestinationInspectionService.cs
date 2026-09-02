using System;
using System.IO;
using System.Linq;

namespace PS3Butler.Services;

public class DestinationInspectionService
{
    public string? FindExistingIso(
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