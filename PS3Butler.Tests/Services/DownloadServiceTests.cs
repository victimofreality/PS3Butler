using System.Net;
using System.Net.Http;
using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class DownloadServiceTests
{
    [Fact]
    public async Task DownloadAsync_DownloadsNamedFileAndReportsProgress()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var contents = Enumerable.Range(0, 4096)
            .Select(value => (byte)(value % 251))
            .ToArray();
        using var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(contents)
            }));
        var service = new DownloadService(client);
        var reports = new List<TransferProgress>();

        var result = await service.DownloadAsync(
            "https://example.test/Game%20Name.iso",
            temporaryDirectory.GetPath("downloads"),
            new InlineProgress<TransferProgress>(reports.Add));

        Assert.True(result.Success, result.Error);
        Assert.Equal("Game Name.iso", Path.GetFileName(result.OutputPath));
        Assert.Equal(contents, File.ReadAllBytes(result.OutputPath));
        Assert.Contains(reports, report => report.Percentage > 0);
        Assert.Equal(100, reports[^1].Percentage);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.test/game.iso")]
    public async Task DownloadAsync_RejectsInvalidOrUnsupportedUrls(string url)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        using var client = CreateClient((_, _) =>
            throw new InvalidOperationException("HTTP should not be called."));
        var service = new DownloadService(client);

        var result = await service.DownloadAsync(
            url,
            temporaryDirectory.GetPath("downloads"));

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.OutputPath);
    }

    [Fact]
    public async Task DownloadAsync_CancellationDeletesPartialFile()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var stream = new BlockingAfterFirstReadStream([1, 2, 3, 4]);
        using var client = CreateClient((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream)
            }));
        var service = new DownloadService(client);
        using var cancellation = new CancellationTokenSource();

        var downloadTask = service.DownloadAsync(
            "https://example.test/game.iso",
            temporaryDirectory.GetPath("downloads"),
            cancellationToken: cancellation.Token);

        await stream.FirstRead.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        var result = await downloadTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(result.Success);
        Assert.Equal("Download cancelled.", result.Error);
        Assert.False(
            File.Exists(
                temporaryDirectory.GetPath("downloads/game.iso")));
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new StubHttpMessageHandler(handler));

    private sealed class BlockingAfterFirstReadStream(byte[] firstChunk) : Stream
    {
        private int _readCount;
        private readonly TaskCompletionSource _firstRead =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstRead => _firstRead.Task;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                firstChunk.AsSpan().CopyTo(buffer.Span);
                _firstRead.TrySetResult();
                return ValueTask.FromResult(firstChunk.Length);
            }

            return WaitForCancellationAsync(cancellationToken);
        }

        private static async ValueTask<int> WaitForCancellationAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush() =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
