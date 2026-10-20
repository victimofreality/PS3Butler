using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Services.Pipeline;

public static class InterruptionSmoke
{
    public static async Task RunAsync(string toolsDirectory)
    {
        var source = Directory.GetFiles(toolsDirectory, "*.iso").Single();
        var root = Path.GetFullPath(Path.Combine("artifacts", "interruption", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        Console.WriteLine($"Test artifacts: {root}");

        await using (var server = new SlowHttpServer())
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var pause = new PauseTokenSource();
            var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reports = 0;
            var progress = new InlineProgress<TransferProgress>(_ =>
            {
                if (++reports == 1)
                {
                    pause.Pause();
                    paused.SetResult();
                }
                else resumed.TrySetResult();
            });
            var destination = Path.Combine(root, "download");
            var download = new DownloadService().DownloadAsync(server.Url, destination, progress, cancellation.Token, pause);
            await paused.Task.WaitAsync(cancellation.Token);
            await Task.Delay(100, cancellation.Token);
            var pausedSize = new FileInfo(Path.Combine(destination, "test.iso")).Length;
            await Task.Delay(150, cancellation.Token);
            Require(!download.IsCompleted && new FileInfo(Path.Combine(destination, "test.iso")).Length == pausedSize, "paused download stops writing");
            pause.Resume();
            await resumed.Task.WaitAsync(cancellation.Token);
            Require(reports > 1, "resumed download continues");
            pause.Pause();
            cancellation.Cancel();
            var result = await download.WaitAsync(TimeSpan.FromSeconds(5));
            Require(!result.Success && result.Error.Contains("cancelled"), "paused download cancellation settles");
            Require(!Directory.EnumerateFiles(destination).Any(), "partial download removed");
        }

        // Portable mode isolates the test runner's metadata from the user's app.
        var marker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        var hadMarker = File.Exists(marker);
        if (!hadMarker) File.WriteAllText(marker, "Interruption smoke runner");
        try
        {
            await using var server = new SlowHttpServer();
            var metadata = new MetadataCacheService(new SettingsService());
            var pipeline = new PipelineRunCoordinator(new KeyIndexService(), metadata, new QueuePreflightCoordinator(metadata));
            var job = new GameJob { SourceType = GameSourceType.Url, SourcePath = server.Url };
            var jobs = new ObservableCollection<GameJob>();
            var store = new QueueStateService(Path.Combine(root, "queue.json"));
            var persistence = new QueuePersistenceCoordinator(store, jobs);
            persistence.Start();
            jobs.Add(job);
            var settings = new PipelineRunSettings
            {
                RedrrxPs3DecPath = Path.Combine(toolsDirectory, "ps3decrs.exe"),
                SevenZipPath = @"C:\Program Files\7-Zip\7z.exe",
                RedumpKeysPath = Path.Combine(toolsDirectory, "Keys"),
                WorkDirectory = Path.Combine(root, "pipeline"),
                IsoDestinationPath = Path.Combine(root, "iso"),
                JbDestinationPath = Path.Combine(root, "jb"),
                Ps3DecThreadCount = 4,
                RemovePs3Update = true
            };
            Directory.CreateDirectory(settings.WorkDirectory);
            var running = pipeline.RunAsync(jobs, settings);
            try
            {
                await WaitUntilAsync(() => Directory.EnumerateFiles(settings.WorkDirectory, "*.iso", SearchOption.AllDirectories).Any(), running);
            }
            finally
            {
                var shutdown = new QueueShutdownCoordinator(pipeline.Cancel, () => running, persistence.StopAsync);
                await shutdown.StopAsync();
            }
            Require(job.Stage == JobStage.Cancelled, "pipeline download shutdown tracks cancellation");
            Require(!Directory.EnumerateFiles(settings.WorkDirectory, "*", SearchOption.AllDirectories).Any(), "pipeline download staging cleaned");
            var restored = store.Load();
            Require(restored.Count == 1 && restored[0].SourceType == GameSourceType.Url &&
                restored[0].SourcePath == server.Url && restored[0].State == JobState.Waiting && restored[0].LocalSourcePath is null,
                "interrupted URL restores as fresh waiting input");
        }
        finally
        {
            if (!hadMarker) File.Delete(marker);
        }

        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
        {
            var destination = Path.Combine(root, "copy-cancel");
            var copy = new DeliveryService().CopyAsync(source, destination, cancellationToken: cancellation.Token);
            try
            {
                await WaitUntilAsync(() => Directory.Exists(destination) && Directory.EnumerateFiles(destination, "*.partial").Any(), copy);
            }
            finally
            {
                cancellation.Cancel();
            }
            var result = await copy;
            Require(!result.Success && result.Error.Contains("cancelled"), "active copy cancellation settles");
            Require(!Directory.EnumerateFileSystemEntries(destination).Any(), "cancelled copy leaves no published or partial output");
        }

        {
            var destination = Path.Combine(root, "copy-race");
            var copy = new DeliveryService().CopyAsync(source, destination);
            await WaitUntilAsync(() => Directory.Exists(destination) && Directory.EnumerateFiles(destination, "*.partial").Any(), copy);
            var finalPath = Path.Combine(destination, Path.GetFileName(source));
            await File.WriteAllTextAsync(finalPath, "Existing file must survive");
            var result = await copy;
            Require(!result.Success, "publish fails when destination appears during copy");
            Require(await File.ReadAllTextAsync(finalPath) == "Existing file must survive", "existing destination preserved");
            Require(!Directory.EnumerateFiles(destination, "*.partial").Any(), "failed publish staging cleaned");
        }
        Console.WriteLine("Interruption smoke checks passed.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, Task operation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (operation.IsCompleted || DateTime.UtcNow >= deadline)
                throw new Exception("Operation did not reach expected active stage.");
            await Task.Delay(1);
        }
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception($"FAILED: {name}");
        Console.WriteLine($"PASS: {name}");
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class SlowHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;
        public string Url { get; }

        public SlowHttpServer()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/test.iso";
            _serving = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                const int totalBytes = 64 * 1024 * 1024;
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {totalBytes}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, _stop.Token);
                var buffer = new byte[64 * 1024];
                for (var sent = 0; sent < totalBytes; sent += buffer.Length)
                {
                    await stream.WriteAsync(buffer, _stop.Token);
                    await Task.Delay(10, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (IOException) { /* The tests intentionally disconnect mid-response. */ }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _serving;
            _stop.Dispose();
        }
    }
}
