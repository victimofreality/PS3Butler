using PS3Butler.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public class FolderDeliveryService
{
    public async Task<(bool Success, string DestinationPath, string Error)> CopyAsync(
        string sourceDirectory,
        string destinationDirectory,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return (
                false,
                string.Empty,
                "Source directory does not exist.");
        }

        if (Directory.Exists(destinationDirectory))
        {
            return (
                false,
                string.Empty,
                $"Destination folder already exists: {destinationDirectory}");
        }

        var stagingDirectory =
            destinationDirectory +
            $".ps3butler-{Guid.NewGuid():N}.partial";

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var files =
                Directory
                    .EnumerateFiles(
                        sourceDirectory,
                        "*",
                        SearchOption.AllDirectories)
                    .ToList();

            var totalBytes =
                files.Sum(
                    path =>
                        new FileInfo(path).Length);

            long copiedBytes = 0;

            Directory.CreateDirectory(
                stagingDirectory);

            var stopwatch =
                Stopwatch.StartNew();

            var lastProgressReport =
                TimeSpan.Zero;

            foreach (var sourceFile in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath =
                    Path.GetRelativePath(
                        sourceDirectory,
                        sourceFile);

                var stagingFile =
                    Path.Combine(
                        stagingDirectory,
                        relativePath);

                var parentDirectory =
                    Path.GetDirectoryName(
                        stagingFile);

                if (!string.IsNullOrWhiteSpace(
                        parentDirectory))
                {
                    Directory.CreateDirectory(
                        parentDirectory);
                }

                const int bufferSize =
                    1024 * 1024;

                var buffer =
                    new byte[bufferSize];

                await using var sourceStream =
                    new FileStream(
                        sourceFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize,
                        useAsync: true);

                await using var stagingStream =
                    new FileStream(
                        stagingFile,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize,
                        useAsync: true);

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

                    var elapsedSeconds =
                        stopwatch.Elapsed.TotalSeconds;

                    var bytesPerSecond =
                        elapsedSeconds > 0
                            ? copiedBytes / elapsedSeconds
                            : 0;

                    var percentage =
                        totalBytes > 0
                            ? copiedBytes *
                              100.0 /
                              totalBytes
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

                    if (stopwatch.Elapsed - lastProgressReport >=
                        TimeSpan.FromMilliseconds(250))
                    {
                        progress?.Report(
                            new TransferProgress
                            {
                                Percentage =
                                    percentage,

                                MegabytesPerSecond =
                                    bytesPerSecond /
                                    (1024 * 1024),

                                EstimatedRemaining =
                                    remaining
                            });

                        lastProgressReport =
                            stopwatch.Elapsed;
                    }
                }

                await stagingStream.FlushAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

            }

            cancellationToken.ThrowIfCancellationRequested();

            /*
             * Validate the staged tree before publishing it.
             */
            var stagedFiles =
                Directory
                    .EnumerateFiles(
                        stagingDirectory,
                        "*",
                        SearchOption.AllDirectories)
                    .ToList();

            if (stagedFiles.Count != files.Count)
            {
                return (
                    false,
                    string.Empty,
                    "Staged JB folder file count does not match source.");
            }

            var stagedBytes =
                stagedFiles.Sum(
                    path =>
                        new FileInfo(path).Length);

            if (stagedBytes != totalBytes)
            {
                return (
                    false,
                    string.Empty,
                    "Staged JB folder size does not match source.");
            }

            /*
             * Same-filesystem rename publishes the complete folder.
             * Directory.Move will fail rather than merge with an
             * existing destination.
             */
            Directory.Move(
                stagingDirectory,
                destinationDirectory);

            progress?.Report(
                new TransferProgress
                {
                    Percentage = 100,
                    EstimatedRemaining =
                        TimeSpan.Zero
                });

            return (
                true,
                destinationDirectory,
                string.Empty);
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(
                stagingDirectory);

            return (
                false,
                string.Empty,
                "Folder copy cancelled.");
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(
                stagingDirectory);

            return (
                false,
                string.Empty,
                $"Folder copy failed: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(
                stagingDirectory);
        }
    }

    private static void TryDeleteDirectory(
        string path)
    {
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
            // Best-effort staging cleanup only.
        }
    }
}