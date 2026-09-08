using PS3Butler.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class DeliveryService
{
    public async Task<(bool Success, string DestinationPath, string Error)> CopyAsync(
        string sourcePath,
        string destinationDirectory,
        IProgress<TransferProgress>? progress = null,
        string? destinationFileName = null,
        CancellationToken cancellationToken = default)
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

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(
            destinationDirectory);

        var destinationPath =
            Path.Combine(
                destinationDirectory,
                string.IsNullOrWhiteSpace(destinationFileName)
                    ? Path.GetFileName(sourcePath)
                    : destinationFileName);

        if (File.Exists(destinationPath))
        {
            return (
                false,
                string.Empty,
                $"Destination file already exists: {destinationPath}");
        }

        var stagingPath =
            destinationPath +
            $".ps3butler-{Guid.NewGuid():N}.partial";

        try
        {
            var sourceInfo =
                new FileInfo(sourcePath);

            var totalBytes =
                sourceInfo.Length;

            long copiedBytes = 0;

            var buffer =
                new byte[1024 * 1024];

            var stopwatch =
                Stopwatch.StartNew();

            var lastProgressReport =
                TimeSpan.Zero;

            await using (
                var sourceStream =
                    new FileStream(
                        sourcePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        buffer.Length,
                        useAsync: true))
            await using (
                var stagingStream =
                    new FileStream(
                        stagingPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        buffer.Length,
                        useAsync: true))
            {
                int bytesRead;

                while ((bytesRead =
                        await sourceStream.ReadAsync(
                            buffer.AsMemory(
                                0,
                                buffer.Length),
                            cancellationToken)
                        .ConfigureAwait(false)) > 0)

                {
                    await stagingStream.WriteAsync(
                            buffer.AsMemory(
                                0,
                                bytesRead),
                            cancellationToken)
                        .ConfigureAwait(false);

                    copiedBytes +=
                        bytesRead;

                    if (totalBytes > 0)
                    {
                        var percentage =
                            copiedBytes *
                            100.0 /
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
                                totalBytes -
                                copiedBytes;

                            estimatedRemaining =
                                TimeSpan.FromSeconds(
                                    remainingBytes /
                                    bytesPerSecond);
                        }

                        if (stopwatch.Elapsed - lastProgressReport >=
                            TimeSpan.FromMilliseconds(250))
                        {
                            progress?.Report(
                                new TransferProgress
                                {
                                    Percentage =
                                        percentage,

                                    MegabytesPerSecond =
                                        megabytesPerSecond,

                                    EstimatedRemaining =
                                        estimatedRemaining
                                });

                            lastProgressReport =
                                stopwatch.Elapsed;
                        }

                    }
                }

                await stagingStream.FlushAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

            }

            stopwatch.Stop();

            cancellationToken.ThrowIfCancellationRequested();

            var stagingInfo =
                new FileInfo(stagingPath);

            if (!stagingInfo.Exists)
            {
                return (
                    false,
                    string.Empty,
                    "Staged destination file was not created.");
            }

            if (sourceInfo.Length !=
                stagingInfo.Length)
            {
                return (
                    false,
                    string.Empty,
                    "Staged destination file size does not match source.");
            }

            /*
             * File.Move without overwrite is the publish operation.
             *
             * The staging file is on the same destination filesystem,
             * so this should be a rename rather than another full copy.
             *
             * If another process creates the final filename between our
             * earlier check and this point, publishing fails rather than
             * overwriting it.
             */
            File.Move(
                stagingPath,
                destinationPath,
                overwrite: false);

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
        catch (OperationCanceledException)
        {
            TryDeleteFile(
                stagingPath);

            return (
                false,
                string.Empty,
                "Copy cancelled.");
        }
        catch (Exception ex)
        {
            TryDeleteFile(
                stagingPath);

            return (
                false,
                string.Empty,
                $"Copy failed: {ex.Message}");
        }
        finally
        {
            /*
             * Harmless after a successful File.Move because stagingPath
             * no longer exists. On any failed path this removes leftovers.
             */
            TryDeleteFile(
                stagingPath);
        }
    }

    private static void TryDeleteFile(
        string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort staging cleanup only.
        }
    }
}