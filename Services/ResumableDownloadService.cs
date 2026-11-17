using PS3Butler.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace PS3Butler.Services;

public sealed class ResumableDownloadService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public ResumableDownloadService() : this(new HttpClient(), true) { }
    public ResumableDownloadService(HttpClient httpClient) : this(httpClient, false) { }
    private ResumableDownloadService(HttpClient httpClient, bool ownsClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsClient = ownsClient;
    }

    public Task<(bool Success, string OutputPath, string Error)> DownloadAsync(
        string url, string workDirectory, IProgress<TransferProgress>? progress = null,
        CancellationToken cancellationToken = default, PauseTokenSource? pauseTokenSource = null) =>
        // Checkpoint/file operations must not run on Avalonia's dispatcher.
        Task.Run(() => DownloadCoreAsync(url, workDirectory, progress, cancellationToken, pauseTokenSource));

    private async Task<(bool Success, string OutputPath, string Error)> DownloadCoreAsync(
        string url, string workDirectory, IProgress<TransferProgress>? progress,
        CancellationToken cancellationToken, PauseTokenSource? pauseTokenSource)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return (false, "", "Only valid HTTP and HTTPS URLs are supported.");

        var directory = DownloadResumeStore.GetDirectory(workDirectory, url);
        var store = new DownloadResumeStore(directory);
        FileStream? lease = null;
        var retainPartial = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(directory);
            // Cross-instance exclusion also protects the checkpoint from a second app.
            lease = new FileStream(Path.Combine(directory, "active.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            var checkpoint = store.Load(url);
            retainPartial = checkpoint is not null;
            if (checkpoint is null) store.Clear();

            // One bounded fresh retry handles ignored/stale/invalid ranges (including 416).
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var offset = checkpoint is null ? 0 : new FileInfo(store.PartialPath).Length;
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
                if (checkpoint is not null)
                {
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                    request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(checkpoint.ETag));
                }
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var responseUrl = (response.RequestMessage?.RequestUri ?? uri).AbsoluteUri;
                var append = checkpoint is not null && IsMatchingRange(response, checkpoint, offset, responseUrl);
                if (checkpoint is not null && !append &&
                    (response.StatusCode == HttpStatusCode.PartialContent || response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable))
                {
                    retainPartial = false;
                    store.Clear();
                    checkpoint = null;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                if (!append && response.StatusCode != HttpStatusCode.OK)
                    throw new IOException("Server did not return a complete file.");
                if (response.Content.Headers.ContentEncoding.Any(value => !value.Equals("identity", StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("Server returned an encoded response; safe byte-range downloads require identity encoding.");

                var fileName = append ? checkpoint!.FileName :
                    DownloadService.TryGetContentDispositionFileName(response) ??
                    DownloadService.TryGetUrlFileName(response.RequestMessage?.RequestUri) ??
                    DownloadService.TryGetUrlFileName(uri);
                if (string.IsNullOrWhiteSpace(fileName))
                    throw new IOException("Could not determine a filename from the HTTP response or URL.");

                var total = append ? checkpoint!.TotalBytes : response.Content.Headers.ContentLength;
                if (!append)
                {
                    retainPartial = false;
                    store.Clear();
                    offset = 0;
                }

                await using (var target = new FileStream(store.PartialPath, append ? FileMode.Open : FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
                {
                    target.Position = offset;
                    if (!append && total is > 0 && response.Headers.ETag is { IsWeak: false } tag && tag.Tag != "*")
                    {
                        store.Save(new DownloadResumeStore.Checkpoint
                        {
                            Url = url, ResponseUrl = responseUrl, FileName = fileName,
                            ETag = tag.ToString(), TotalBytes = total.Value
                        });
                        retainPartial = true;
                    }

                    await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                    var clock = Stopwatch.StartNew();
                    var downloaded = offset;
                    var buffer = new byte[1024 * 1024];
                    Report(downloaded);
                    while (true)
                    {
                        if (pauseTokenSource is not null)
                            await pauseTokenSource.WaitWhilePausedAsync(cancellationToken);
                        var count = await source.ReadAsync(buffer, cancellationToken);
                        if (count == 0) break;
                        if (total.HasValue && count > total.Value - downloaded)
                        {
                            retainPartial = false;
                            throw new IOException("Server sent more bytes than expected.");
                        }
                        await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                        downloaded += count;
                        Report(downloaded);
                    }
                    if (total.HasValue && downloaded != total.Value)
                        throw new IOException("Server ended the download before the expected file length.");
                    await target.FlushAsync(cancellationToken);

                    void Report(long bytes)
                    {
                        var speed = clock.Elapsed.TotalSeconds > 0 ? (bytes - offset) / clock.Elapsed.TotalSeconds : 0;
                        progress?.Report(new TransferProgress
                        {
                            Percentage = total is > 0 ? bytes * 100.0 / total.Value : 0,
                            MegabytesPerSecond = speed / (1024 * 1024),
                            EstimatedRemaining = total.HasValue && speed > 0 ? TimeSpan.FromSeconds((total.Value - bytes) / speed) : null
                        });
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                // Unique completed folder preserves the server filename for key lookup,
                // without overwriting an earlier completed download or user file.
                var completedDirectory = Path.Combine(directory, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(completedDirectory);
                var outputPath = Path.Combine(completedDirectory, fileName);
                File.Move(store.PartialPath, outputPath);
                retainPartial = false;
                store.Clear();
                progress?.Report(new TransferProgress { Percentage = 100, EstimatedRemaining = TimeSpan.Zero });
                return (true, outputPath, "");
            }
            throw new IOException("Server returned an invalid byte range.");
        }
        catch (OperationCanceledException)
        {
            if (lease is not null) retainPartial = retainPartial && store.Load(url) is not null;
            return (false, "", "Download cancelled." + (retainPartial ? " Partial download saved; retry to resume." : ""));
        }
        catch (Exception ex)
        {
            if (lease is not null) retainPartial = retainPartial && store.Load(url) is not null;
            return (false, "", $"Download failed: {ex.Message}" + (retainPartial ? " Partial download saved; retry to resume." : ""));
        }
        finally
        {
            if (lease is not null)
            {
                try { if (!retainPartial) store.Clear(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { /* Best-effort removal of unusable temporary data only. */ }
                lease.Dispose();
            }
        }
    }

    private static bool IsMatchingRange(HttpResponseMessage response, DownloadResumeStore.Checkpoint checkpoint, long offset, string responseUrl)
    {
        var range = response.Content.Headers.ContentRange;
        return response.StatusCode == HttpStatusCode.PartialContent &&
            responseUrl == checkpoint.ResponseUrl &&
            response.Headers.ETag?.ToString() == checkpoint.ETag &&
            range is { HasRange: true, HasLength: true } &&
            range.Unit.Equals("bytes", StringComparison.OrdinalIgnoreCase) &&
            range.From == offset && range.To == checkpoint.TotalBytes - 1 && range.Length == checkpoint.TotalBytes &&
            (!response.Content.Headers.ContentLength.HasValue || response.Content.Headers.ContentLength == checkpoint.TotalBytes - offset);
    }

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }
}
