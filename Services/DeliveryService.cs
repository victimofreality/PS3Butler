using PS3Butler.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class DeliveryService
{
    public async Task<(bool Success, string DestinationPath, string Error)> CopyAsync(
        string sourcePath,
        string destinationDirectory,
        IProgress<TransferProgress>? progress = null)
    {
        if (!File.Exists(sourcePath))
        {
            return (
                false,
                string.Empty,
                "Source file does not exist.");
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            return (
                false,
                string.Empty,
                "Destination directory is not configured.");
        }

        Directory.CreateDirectory(destinationDirectory);

        var destinationPath =
            Path.Combine(
                destinationDirectory,
                Path.GetFileName(sourcePath));

        try
        {
            var sourceInfo = new FileInfo(sourcePath);
            var totalBytes = sourceInfo.Length;

            long copiedBytes = 0;

            var buffer = new byte[1024 * 1024];

            var stopwatch = Stopwatch.StartNew();

            await using var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                buffer.Length,
                useAsync: true);

            await using var destinationStream = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                buffer.Length,
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

                if (totalBytes > 0)
                {
                    var percentage =
                        copiedBytes * 100.0 /
                        totalBytes;

                    var elapsedSeconds =
                        stopwatch.Elapsed.TotalSeconds;

                    var bytesPerSecond =
                        elapsedSeconds > 0
                            ? copiedBytes / elapsedSeconds
                            : 0;

                    var megabytesPerSecond =
                        bytesPerSecond /
                        (1024 * 1024);

                    TimeSpan? estimatedRemaining =
                        null;

                    if (bytesPerSecond > 0)
                    {
                        var remainingBytes =
                            totalBytes - copiedBytes;

                        estimatedRemaining =
                            TimeSpan.FromSeconds(
                                remainingBytes /
                                bytesPerSecond);
                    }

                    progress?.Report(
                        new TransferProgress
                        {
                            Percentage = percentage,
                            MegabytesPerSecond =
                                megabytesPerSecond,
                            EstimatedRemaining =
                                estimatedRemaining
                        });
                }
            }

            await destinationStream.FlushAsync();

            stopwatch.Stop();
        }
        catch (Exception ex)
        {
            return (
                false,
                string.Empty,
                $"Copy failed: {ex.Message}");
        }

        var finalSourceInfo =
            new FileInfo(sourcePath);

        var destinationInfo =
            new FileInfo(destinationPath);

        if (!destinationInfo.Exists)
        {
            return (
                false,
                string.Empty,
                "Destination file was not created.");
        }

        if (finalSourceInfo.Length !=
            destinationInfo.Length)
        {
            return (
                false,
                string.Empty,
                "Destination file size does not match source.");
        }

        progress?.Report(
            new TransferProgress
            {
                Percentage = 100,
                MegabytesPerSecond = 0,
                EstimatedRemaining =
                    TimeSpan.Zero
            });

        return (
            true,
            destinationPath,
            string.Empty);
    }
}