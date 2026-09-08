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

        string? outputPath = null;

        try
        {
            using var response =
                await _httpClient.GetAsync(
                    uri,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

            response.EnsureSuccessStatusCode();

            /*
             * Prefer the filename supplied by the server. This covers download
             * URLs whose path is just an ID, token, or script endpoint.
             *
             * Fall back to the final response URI, then the originally requested
             * URI when Content-Disposition does not provide a usable filename.
             */
            var fileName =
                TryGetContentDispositionFileName(response) ??
                TryGetUrlFileName(
                    response.RequestMessage?.RequestUri) ??
                TryGetUrlFileName(uri);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return (
                    false,
                    string.Empty,
                    "Could not determine a filename from the HTTP response or URL.");
            }

            outputPath =
                Path.Combine(
                    destinationDirectory,
                    fileName);

            var totalBytes =
                response.Content.Headers.ContentLength;

            await using var sourceStream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

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
            TryDeletePartialDownload(
                outputPath);

            return (
                false,
                string.Empty,
                "Download cancelled.");
        }
        catch (Exception ex)
        {
            TryDeletePartialDownload(
                outputPath);

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
            outputPath!,
            string.Empty);
    }

    private static string? TryGetContentDispositionFileName(
        HttpResponseMessage response)
    {
        var contentDisposition =
            response.Content.Headers.ContentDisposition;

        var candidate =
            contentDisposition?.FileNameStar ??
            contentDisposition?.FileName;

        return SanitizeFileName(
            candidate);
    }

    private static string? TryGetUrlFileName(
        Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        try
        {
            return SanitizeFileName(
                Uri.UnescapeDataString(
                    uri.AbsolutePath));
        }
        catch
        {
            return null;
        }
    }

    private static string? SanitizeFileName(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate =
            value.Trim().Trim('"');

        /*
         * Some servers expose filename*=UTF-8''name%20here.iso.
         * FileNameStar is normally decoded by HttpClient, but accepting the
         * raw RFC 5987 form costs almost nothing and makes this more robust.
         */
        var encodedSeparatorIndex =
            candidate.IndexOf(
                "''",
                StringComparison.Ordinal);

        if (encodedSeparatorIndex >= 0 &&
            encodedSeparatorIndex + 2 < candidate.Length)
        {
            candidate =
                candidate[
                    (encodedSeparatorIndex + 2)..];
        }

        try
        {
            candidate =
                Uri.UnescapeDataString(
                    candidate);
        }
        catch
        {
            // Keep the original candidate if percent-decoding is malformed.
        }

        /*
         * Never allow a server-provided name to escape the download directory.
         * Treat both slash styles as separators regardless of host platform.
         */
        candidate =
            candidate.Replace(
                '\\',
                '/');

        var lastSlash =
            candidate.LastIndexOf('/');

        if (lastSlash >= 0)
        {
            candidate =
                candidate[
                    (lastSlash + 1)..];
        }

        foreach (var invalidCharacter in
                 Path.GetInvalidFileNameChars())
        {
            candidate =
                candidate.Replace(
                    invalidCharacter,
                    '_');
        }

        candidate =
            candidate.Trim();

        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate == "." ||
            candidate == "..")
        {
            return null;
        }

        return candidate;
    }

    private static void TryDeletePartialDownload(
        string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

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
    }

}