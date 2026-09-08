using System;
using System.IO;
using System.Linq;

namespace PS3Butler.Services;

public static class TemporaryDownloadCleanupService
{
    public static void CleanupFile(
        string? filePath,
        string workDirectory)
    {
        if (string.IsNullOrWhiteSpace(filePath) ||
            string.IsNullOrWhiteSpace(workDirectory))
        {
            return;
        }

        try
        {
            var downloadRoot =
                Path.GetFullPath(
                    Path.Combine(
                        workDirectory,
                        "download"));

            var fullFilePath =
                Path.GetFullPath(
                    filePath);

            if (!IsInsideDirectory(
                    fullFilePath,
                    downloadRoot))
            {
                return;
            }

            var downloadDirectory =
                Path.GetDirectoryName(
                    fullFilePath);

            if (File.Exists(fullFilePath))
            {
                File.Delete(
                    fullFilePath);
            }

            if (!string.IsNullOrWhiteSpace(
                    downloadDirectory) &&
                Directory.Exists(
                    downloadDirectory) &&
                !Directory.EnumerateFileSystemEntries(
                    downloadDirectory).Any())
            {
                Directory.Delete(
                    downloadDirectory);
            }
        }
        catch
        {
            // Best-effort temporary cleanup only.
        }
    }

    public static void CleanupDirectory(
        string? directoryPath,
        string workDirectory)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) ||
            string.IsNullOrWhiteSpace(workDirectory))
        {
            return;
        }

        try
        {
            var downloadRoot =
                Path.GetFullPath(
                    Path.Combine(
                        workDirectory,
                        "download"));

            var fullDirectoryPath =
                Path.GetFullPath(
                    directoryPath);

            if (!IsInsideDirectory(
                    fullDirectoryPath,
                    downloadRoot))
            {
                return;
            }

            if (Directory.Exists(
                    fullDirectoryPath))
            {
                Directory.Delete(
                    fullDirectoryPath,
                    recursive: true);
            }
        }
        catch
        {
            // Best-effort temporary cleanup only.
        }
    }

    private static bool IsInsideDirectory(
        string candidatePath,
        string rootPath)
    {
        var normalizedRoot =
            rootPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        return candidatePath.StartsWith(
            normalizedRoot,
            StringComparison.OrdinalIgnoreCase);
    }
}