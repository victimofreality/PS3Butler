using System.Net;
using System.Net.Sockets;
using System.Text;
using PS3Butler.Models;
using PS3Butler.Services;

internal static class DownloadFailureSmoke
{
    public static async Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "download-failures", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var downloader = new DownloadService();
        var checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Console.WriteLine($"PASS: {label}");
            checks++;
        }

        foreach (var status in new[] { 403, 404, 500 })
        {
            await using var server = new Server($"HTTP/1.1 {status} Rejected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            var directory = Path.Combine(root, status.ToString());
            var result = await downloader.DownloadAsync(server.Url, directory).WaitAsync(TimeSpan.FromSeconds(10));
            Check(!result.Success && result.OutputPath == "" && result.Error.Contains(status.ToString()), $"HTTP {status} has actionable failure");
            Check(!Directory.EnumerateFiles(directory).Any(), $"HTTP {status} leaves no download");
        }

        foreach (var chunked in new[] { false, true })
        {
            var response = chunked
                ? "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n10\r\nshort"
                : "HTTP/1.1 200 OK\r\nContent-Length: 100000\r\nConnection: close\r\n\r\nshort";
            await using var server = new Server(response);
            var directory = Path.Combine(root, chunked ? "chunked" : "length");
            var result = await downloader.DownloadAsync(server.Url, directory).WaitAsync(TimeSpan.FromSeconds(10));
            Check(!result.Success && result.Error.StartsWith("Download failed:"), $"truncated {(chunked ? "chunked" : "known-length")} body reports failure");
            Check(!Directory.EnumerateFiles(directory).Any(), "truncated partial file removed");
        }

        foreach (var headersSent in new[] { false, true })
        {
            await using var server = new Server(headersSent
                ? "HTTP/1.1 200 OK\r\nContent-Length: 100000\r\nConnection: close\r\n\r\nshort"
                : "", holdOpen: true);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var directory = Path.Combine(root, headersSent ? "cancel-body" : "cancel-headers");
            var pending = downloader.DownloadAsync(server.Url, directory, cancellationToken: cancellation.Token);
            await server.ResponseSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!result.Success && result.Error == "Download cancelled.", $"cancellation waiting for {(headersSent ? "body" : "headers")} settles");
            Check(!Directory.EnumerateFiles(directory).Any(), "cancellation leaves no partial download");
        }

        await using (var server = new Server("HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: close\r\n\r\nokay"))
        {
            var result = await downloader.DownloadAsync(server.Url, Path.Combine(root, "retry")).WaitAsync(TimeSpan.FromSeconds(10));
            Check(result.Success && await File.ReadAllTextAsync(result.OutputPath) == "okay", "same downloader succeeds after network failures and cancellation");
        }

        var store = new QueueStateService(Path.Combine(root, "queue.json"));
        store.Save([new GameJob { SourcePath = "https://example.invalid/game.iso", SourceType = GameSourceType.Url, State = JobState.Error, Stage = JobStage.Error, Details = "Download failed" }]);
        var restored = store.Load();
        Check(restored.Count == 1 && restored[0].State == JobState.Waiting && restored[0].LocalSourcePath is null,
            "failed download remains retryable after queue restoration");
        Console.WriteLine($"Download failure smoke: {checks} checks passed. Fixtures: {root}");
    }

    private sealed class Server : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        public TaskCompletionSource ResponseSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Url { get; }

        public Server(string response, bool holdOpen = false)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/game.iso";
            _serve = ServeAsync(response, holdOpen);
        }

        private async Task ServeAsync(string response, bool holdOpen)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
                ResponseSent.TrySetResult();
                if (holdOpen) await Task.Delay(Timeout.Infinite, _stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _serve;
            _stop.Dispose();
        }
    }
}
