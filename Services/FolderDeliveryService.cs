using PS3Butler.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class FolderDeliveryService
{
    public async Task<(bool Success, string DestinationPath, string Error)> CopyAsync(
        string sourceDirectory,
        string destinationDirectory,
        IProgress<TransferProgress>? progress = null)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return (
                false,
                string.Empty,
                "Source directory does not exist.");
        }

        try
        {
            var files =
                Directory
                    .EnumerateFiles(
                        sourceDirectory,
                        "*",
                        SearchOption.AllDirectories)
                    .ToList();

            var totalBytes =
                files.Sum(path => new FileInfo(path).Length);

            long copiedBytes = 0;

            Directory.CreateDirectory(destinationDirectory);

            var stopwatch =
                Stopwatch.StartNew();

            foreach (var sourceFile in files)
            {
                var relativePath =
                    Path.GetRelativePath(
                        sourceDirectory,
                        sourceFile);

                var destinationFile =
                    Path.Combine(
                        destinationDirectory,
                        relativePath);

                var parentDirectory =
                    Path.GetDirectoryName(destinationFile);

                if (!string.IsNullOrWhiteSpace(parentDirectory))
                {
                    Directory.CreateDirectory(parentDirectory);
                }

                const int bufferSize = 1024 * 1024;
                var buffer = new byte[bufferSize];

                await using var sourceStream =
                    new FileStream(
                        sourceFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize,
                        useAsync: true);

                await using var destinationStream =
                    new FileStream(
                        destinationFile,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize,
                        useAsync: true);

                int bytesRead;

                while ((bytesRead =
                        await sourceStream.ReadAsync(
                            buffer.AsMemory(
                                0,
                                buffer.Length))) > 0)
                {
                    await destinationStream.WriteAsync(
                        buffer.AsMemory(
                            0,
                            bytesRead));

                    copiedBytes += bytesRead;

                    var elapsedSeconds =
                        stopwatch.Elapsed.TotalSeconds;

                    var bytesPerSecond =
                        elapsedSeconds > 0
                            ? copiedBytes / elapsedSeconds
                            : 0;

                    var percentage =
                        totalBytes > 0
                            ? copiedBytes * 100.0 / totalBytes
                            : 100;

                    TimeSpan? remaining =
                        null;

                    if (bytesPerSecond > 0)
                    {
                        remaining =
                            TimeSpan.FromSeconds(
                                (totalBytes - copiedBytes) /
                                bytesPerSecond);
                    }

                    progress?.Report(
                        new TransferProgress
                        {
                            Percentage = percentage,
                            MegabytesPerSecond =
                                bytesPerSecond / (1024 * 1024),
                            EstimatedRemaining = remaining
                        });
                }
            }

            progress?.Report(
                new TransferProgress
                {
                    Percentage = 100,
                    EstimatedRemaining = TimeSpan.Zero
                });

            return (
                true,
                destinationDirectory,
                string.Empty);
        }
        catch (Exception ex)
        {
            return (
                false,
                string.Empty,
                $"Folder copy failed: {ex.Message}");
        }
    }
}