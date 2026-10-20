using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Services.Pipeline;

public static class PipelineOrderingSmoke
{
    public static async Task RunAsync(string toolsDirectory, string? decryptor)
    {
        var source = Directory.GetFiles(toolsDirectory, "*.iso").Single();
        var root = Path.GetFullPath(Path.Combine("artifacts", "ordering", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        Console.WriteLine($"Ordering artifacts: {root}");
        var marker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        var hadMarker = File.Exists(marker);
        if (!hadMarker) File.WriteAllText(marker, "Ordering smoke runner");
        try
        {
            var sourceHash = await HashAsync(source);
            await RunScenario(cancel: false);
            await RunScenario(cancel: true);
            Require(await HashAsync(source) == sourceHash, "ordering tests preserve original source ISO");
            Console.WriteLine("Pipeline ordering smoke checks passed.");

            async Task RunScenario(bool cancel)
            {
                var scenario = cancel ? "cancel" : "complete";
                Console.WriteLine($"RUN ordering: {scenario}");
                var local = Path.Combine(root, scenario);
                Directory.CreateDirectory(local);
                await using var server = new IsoServer(source);
                var jobs = new ObservableCollection<GameJob>();
                for (var index = 0; index < 4; index++)
                {
                    var destination = Path.Combine(local, "output-" + index);
                    Directory.CreateDirectory(destination);
                    jobs.Add(new GameJob
                    {
                        SourcePath = index < 2 ? source : server.Url(index - 2),
                        SourceType = index < 2 ? GameSourceType.LocalFile : GameSourceType.Url,
                        OutputFormat = OutputFormat.DecryptedIso,
                        DestinationOverride = destination
                    });
                }
                var activity = new Activity(jobs);
                var store = new QueueStateService(Path.Combine(local, "queue.json"));
                var persistence = new QueuePersistenceCoordinator(store, jobs);
                persistence.Start();
                // Start() restores into an existing collection; this fixture starts
                // with no saved file and StopAsync still takes a final snapshot.
                var metadata = new MetadataCacheService(new SettingsService());
                var pipeline = new PipelineRunCoordinator(new KeyIndexService(), metadata, new QueuePreflightCoordinator(metadata));
                var work = Path.Combine(local, "work");
                Directory.CreateDirectory(work);
                var settings = new PipelineRunSettings
                {
                    RedrrxPs3DecPath = decryptor ?? Path.Combine(toolsDirectory, "ps3decrs.exe"),
                    SevenZipPath = @"C:\Program Files\7-Zip\7z.exe",
                    RedumpKeysPath = Path.Combine(toolsDirectory, "Keys"),
                    WorkDirectory = work,
                    IsoDestinationPath = Path.Combine(local, "default-iso"),
                    JbDestinationPath = Path.Combine(local, "default-jb"),
                    Ps3DecThreadCount = 4,
                    RemovePs3Update = true
                };
                var running = pipeline.RunAsync(jobs, settings);
                if (cancel)
                {
                    try
                    {
                        await activity.Overlap.Task.WaitAsync(TimeSpan.FromSeconds(20));
                        await server.Started.Task.WaitAsync(TimeSpan.FromSeconds(20));
                    }
                    finally
                    {
                        await new QueueShutdownCoordinator(pipeline.Cancel, () => running, persistence.StopAsync).StopAsync();
                    }
                    Require(jobs[0].Stage == JobStage.Cancelled && jobs[1].Stage == JobStage.Cancelled, "cancel settles active and gated processing jobs");
                    Require(jobs[2].Stage == JobStage.Cancelled && jobs[3].State == JobState.Waiting, "cancel settles active download and leaves unstarted URL waiting");
                    Require(store.Load().Count == 4 && store.Load().All(job => job.State == JobState.Waiting), "mixed cancellation restores all four inputs waiting");
                    Require(jobs.All(job => !Directory.EnumerateFileSystemEntries(job.DestinationOverride!).Any()), "mixed cancellation publishes no outputs");
                }
                else
                {
                    try
                    {
                        await running.WaitAsync(TimeSpan.FromMinutes(2));
                    }
                    finally
                    {
                        if (!running.IsCompleted) pipeline.Cancel();
                        await running;
                        await persistence.StopAsync();
                    }
                    Require(jobs.All(job => job.State == JobState.Complete), "two local and two URL jobs all complete");
                    Require(activity.ProcessingOrder.SequenceEqual(new[] { 0, 1, 2, 3 }), "processing preserves local queue and URL arrival order");
                    Require(server.RequestOrder.SequenceEqual(new[] { 0, 1 }), "HTTP requests follow URL queue order");
                    var expectedHash = await HashAsync(jobs[0].OutputPath!);
                    Require(expectedHash != sourceHash, "completed outputs are decrypted rather than source copies");
                    foreach (var job in jobs)
                        Require(await HashAsync(job.OutputPath!) == expectedHash, $"job {jobs.IndexOf(job)} output matches local decrypted ISO");
                    Require(store.Load().Count == 0, "completed mixed queue does not restore as pending work");
                }
                Require(activity.MaxDownloads == 1 && server.MaxActive == 1, $"{scenario}: exactly one active downloader");
                Require(activity.MaxProcessing == 1, $"{scenario}: exactly one active processing job");
                Require(activity.Overlap.Task.IsCompletedSuccessfully, $"{scenario}: download and processing overlap observed");
                Require(!Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).Any() && jobs.All(job => job.LocalSourcePath is null),
                    $"{scenario}: work files and URL local paths cleaned");
                activity.Detach();
            }
        }
        finally
        {
            if (!hadMarker) File.Delete(marker);
        }
    }

    private sealed class Activity
    {
        private readonly IReadOnlyList<GameJob> _jobs;
        private readonly Dictionary<GameJob, (bool Download, bool Process)> _active = [];
        private readonly object _gate = new();
        public int MaxDownloads { get; private set; }
        public int MaxProcessing { get; private set; }
        public List<int> ProcessingOrder { get; } = [];
        public TaskCompletionSource Overlap { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Activity(IReadOnlyList<GameJob> jobs)
        {
            _jobs = jobs;
            foreach (var job in jobs)
            {
                _active[job] = (false, false);
                job.PropertyChanged += Changed;
            }
        }

        private void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not GameJob job || e.PropertyName is not (nameof(GameJob.Stage) or nameof(GameJob.State))) return;
            lock (_gate)
            {
                var download = job.State == JobState.Running && job.Stage == JobStage.Downloading;
                var process = job.State == JobState.Running && job.Stage is JobStage.Decrypting or JobStage.Copying or JobStage.CleaningUp;
                if (process && !_active[job].Process) ProcessingOrder.Add(_jobs.ToList().IndexOf(job));
                _active[job] = (download, process);
                var downloads = _active.Values.Count(value => value.Download);
                var processing = _active.Values.Count(value => value.Process);
                MaxDownloads = Math.Max(MaxDownloads, downloads);
                MaxProcessing = Math.Max(MaxProcessing, processing);
                if (downloads > 0 && processing > 0) Overlap.TrySetResult();
            }
        }

        public void Detach()
        {
            foreach (var job in _jobs) job.PropertyChanged -= Changed;
        }
    }

    private sealed class IsoServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _accepting;
        private readonly List<Task> _requests = [];
        private readonly string _source;
        private int _active;
        public int MaxActive { get; private set; }
        public List<int> RequestOrder { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IsoServer(string source)
        {
            _source = source;
            _listener.Start();
            _accepting = AcceptAsync();
        }

        public string Url(int index) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/{index}";

        private async Task AcceptAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                    _requests.Add(ServeAsync(await _listener.AcceptTcpClientAsync(_stop.Token)));
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var request = await reader.ReadLineAsync(_stop.Token) ?? throw new IOException("Missing HTTP request");
                    while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 }) { }
                    lock (RequestOrder)
                    {
                        RequestOrder.Add(int.Parse(request.Split(' ')[1].Trim('/')));
                        MaxActive = Math.Max(MaxActive, ++_active);
                    }
                    try
                    {
                        var filename = Uri.EscapeDataString(Path.GetFileName(_source));
                        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {new FileInfo(_source).Length}\r\nContent-Disposition: attachment; filename*=UTF-8''{filename}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(headers, _stop.Token);
                        Started.TrySetResult();
                        await using var input = File.OpenRead(_source);
                        var buffer = new byte[1024 * 1024];
                        int count;
                        while ((count = await input.ReadAsync(buffer, _stop.Token)) > 0)
                        {
                            await Task.Delay(8, _stop.Token);
                            await stream.WriteAsync(buffer.AsMemory(0, count), _stop.Token);
                        }
                    }
                    finally
                    {
                        lock (RequestOrder) _active--;
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
                catch (IOException) { /* Cancellation intentionally disconnects clients. */ }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            await _accepting;
            await Task.WhenAll(_requests);
            _stop.Dispose();
        }
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception($"FAILED: {name}");
        Console.WriteLine($"PASS: {name}");
    }
}
