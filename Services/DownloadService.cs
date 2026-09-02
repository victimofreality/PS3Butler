using PS3Butler.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Threading;

namespace PS3Butler.Services;

public class DownloadService
{
    private readonly HttpClient _httpClient = new();

    public async Task<(bool Success, string OutputPath, string Error)> DownloadAsync(
        string url,
        string destinationDirectory,
        IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default,
        PauseTokenSource? pauseTokenSource = null)
    {
        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
        {
            return (
                false,
                string.Empty,
                "Invalid URL.");
        }

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return (
                false,
                string.Empty,
                "Only HTTP and HTTPS URLs are supported.");
        }

        Directory.CreateDirectory(destinationDirectory);

        string fileName;

        try
        {
            fileName =
                Path.GetFileName(
                    Uri.UnescapeDataString(
                        uri.AbsolutePath));
        }
        catch
        {
            fileName = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return (
                false,
                string.Empty,
                "Could not determine a filename from the URL.");
        }

        var outputPath =
            Path.Combine(
                destinationDirectory,
                fileName);

        try
        {
            using var response =
                await _httpClient.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            var totalBytes =
                response.Content.Headers.ContentLength;

            await using var sourceStream =
                await response.Content.ReadAsStreamAsync();

            await using var destinationStream =
                new FileStream(
                    outputPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 1024,
                    useAsync: true);

            var buffer =
                new byte[1024 * 1024];

            long downloadedBytes = 0;

            var stopwatch =
                Stopwatch.StartNew();

            int bytesRead;

            while ((bytesRead =
                    await sourceStream.ReadAsync(
                        buffer.AsMemory(
                            0,
                            buffer.Length),
                        cancellationToken)) > 0)
            {
                if (pauseTokenSource is not null)
                {
                    await pauseTokenSource.WaitWhilePausedAsync(
                        cancellationToken);
                }

                await destinationStream.WriteAsync(
                    buffer.AsMemory(
                        0,
                        bytesRead),
                    cancellationToken);

                downloadedBytes +=
                    bytesRead;

                if (totalBytes.HasValue &&
                    totalBytes.Value > 0)
                {
                    var percentage =
                        downloadedBytes *
                        100.0 /
                        totalBytes.Value;

                    var elapsedSeconds =
                        stopwatch.Elapsed.TotalSeconds;

                    var bytesPerSecond =
                        elapsedSeconds > 0
                            ? downloadedBytes /
                              elapsedSeconds
                            : 0;

                    var megabytesPerSecond =
                        bytesPerSecond /
                        (1024 * 1024);

                    TimeSpan? estimatedRemaining =
                        null;

                    if (bytesPerSecond > 0)
                    {
                        var remainingBytes =
                            totalBytes.Value -
                            downloadedBytes;

                        estimatedRemaining =
                            TimeSpan.FromSeconds(
                                remainingBytes /
                                bytesPerSecond);
                    }

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
                }
            }

            await destinationStream.FlushAsync(
                cancellationToken);

            stopwatch.Stop();
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }

            return (
                false,
                string.Empty,
                "Download cancelled.");
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }

            return (
                false,
                string.Empty,
                $"Download failed: {ex.Message}");
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
            outputPath,
            string.Empty);
    }
}