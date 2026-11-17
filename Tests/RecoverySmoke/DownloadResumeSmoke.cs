using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Services.Execution;
using PS3Butler.Services.Pipeline;

internal static class DownloadResumeSmoke
{
    private static int _checks;
    private static readonly byte[] Payload = MakePayload();

    public static async Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "download-resume", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        foreach (var mode in new[] { "resume", "percent", "ignored", "changed", "wrong-start", "wrong-total", "wrong-tag", "416", "corrupt", "oversized", "weak", "no-tag", "unknown-length" })
        {
            var work = Path.Combine(root, mode);
            await using var server = new Server(mode);
            using (var first = new ResumableDownloadService())
            {
                var failed = await first.DownloadAsync(server.Url, work).WaitAsync(TimeSpan.FromSeconds(10));
                Check(!failed.Success, $"{mode}: interrupted transfer reports failure ({failed.Error})");
            }
            var resumable = mode is not ("weak" or "no-tag" or "unknown-length");
            var partial = Directory.GetFiles(work, "download.part", SearchOption.AllDirectories).SingleOrDefault();
            Check(resumable ? partial is not null && new FileInfo(partial).Length == Server.PrefixLength : partial is null,
                $"{mode}: only validated resumable bytes retained");
            if (mode == "corrupt")
                await File.WriteAllTextAsync(Directory.GetFiles(work, "resume.json", SearchOption.AllDirectories).Single(), "broken JSON");
            if (mode == "oversized")
            {
                using var file = new FileStream(partial!, FileMode.Open, FileAccess.Write);
                file.SetLength(Payload.Length + 1);
            }

            var store = new QueueStateService(Path.Combine(root, mode + "-queue.json"));
            store.Save([new GameJob { SourcePath = server.Url, SourceType = GameSourceType.Url, State = JobState.Error }]);
            var restored = store.Load().Single();
            Check(restored.State == JobState.Waiting && restored.LocalSourcePath is null,
                $"{mode}: queue restore retains only inputs");
            using var restarted = new ResumableDownloadService();
            var result = await restarted.DownloadAsync(restored.SourcePath, work).WaitAsync(TimeSpan.FromSeconds(10));
            Check(result.Success && (await File.ReadAllBytesAsync(result.OutputPath)).SequenceEqual(mode == "changed" ? server.ChangedPayload : Payload),
                $"{mode}: retry produces exact complete representation ({result.Error})");
            Check(Path.GetFileName(result.OutputPath) == (mode == "percent" ? "Game%20.iso" : "Game café.iso"), $"{mode}: response filename preserved");
            Check(!Directory.GetFiles(work, "*.part", SearchOption.AllDirectories).Any() &&
                !Directory.GetFiles(work, "resume.json", SearchOption.AllDirectories).Any(), $"{mode}: successful transfer clears checkpoint");
            var expectedRange = resumable && mode is not ("corrupt" or "oversized");
            Check(server.Requests[1].Range == (expectedRange ? $"bytes={Server.PrefixLength}-" : "") &&
                server.Requests[1].IfRange == (expectedRange ? "\"fixture-v1\"" : ""), $"{mode}: retry sends safe Range and If-Range headers");
            if (mode == "resume") Check(server.BytesSent == Payload.Length, "resume: restart avoids retransmitting the prefix");
            if (mode is "wrong-start" or "wrong-total" or "wrong-tag" or "416")
                Check(server.Requests.Count == 3 && server.Requests[2].Range == "", $"{mode}: invalid range triggers one fresh request");
            TemporaryDownloadCleanupService.CleanupFile(result.OutputPath, work);
            Check(!Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories).Any(), $"{mode}: normal processing cleanup leaves no cache files");
        }

        await CheckCancelAndPauseAsync(Path.Combine(root, "cancel"));
        await CheckHeaderFailureAsync(Path.Combine(root, "headers"));
        await CheckExclusiveAccessAsync(Path.Combine(root, "exclusive"));
        await CheckPipelineRestartAsync(Path.Combine(root, "pipeline"));
        await CheckCrashRestartAsync(Path.Combine(root, "process-exit"));
        Console.WriteLine($"Download resume smoke: {_checks} checks passed. Fixtures: {root}");
    }

    public static async Task RunInterruptedChildAsync(string url, string work)
    {
        var pause = new PauseTokenSource();
        using var downloader = new ResumableDownloadService();
        await downloader.DownloadAsync(url, work, new InlineProgress(p =>
        {
            if (p.Percentage >= 60)
            {
                pause.Pause();
                Console.WriteLine("TRANSFER_READY");
            }
        }), pauseTokenSource: pause);
    }

    private static async Task CheckCrashRestartAsync(string work)
    {
        await using var server = new Server("slow");
        using var kill = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ready = false;
        var arguments = new List<string>();
        var executable = Environment.ProcessPath!;
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments.Add(typeof(DownloadResumeSmoke).Assembly.Location);
        arguments.AddRange(["--resume-child", server.Url, work]);
        await new ProcessRunner().RunAsync(new ProcessRunRequest
        {
            FileName = executable, ArgumentList = arguments, Timeout = TimeSpan.FromSeconds(20),
            StandardOutputChunkReceived = chunk =>
            {
                if (chunk.Contains("TRANSFER_READY")) { ready = true; kill.Cancel(); }
            }
        }, kill.Token);
        Check(ready, "child process reached a durable partial before forced termination");
        var partial = Directory.GetFiles(work, "download.part", SearchOption.AllDirectories).Single();
        var retained = new FileInfo(partial).Length;
        Check(retained > 0 && retained < Payload.Length, "abrupt process exit leaves checkpoint and partial bytes");
        using var restart = new ResumableDownloadService();
        var result = await restart.DownloadAsync(server.Url, work).WaitAsync(TimeSpan.FromSeconds(10));
        Check(result.Success && (await File.ReadAllBytesAsync(result.OutputPath)).SequenceEqual(Payload) &&
            server.Requests[1].Range == $"bytes={retained}-", "new process resumes from flushed bytes after forced termination");
    }

    private static async Task CheckCancelAndPauseAsync(string work)
    {
        await using var server = new Server("slow");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var pause = new PauseTokenSource();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var downloader = new ResumableDownloadService())
        {
            var download = downloader.DownloadAsync(server.Url, work, new InlineProgress(value =>
            {
                if (value.Percentage > 0) { pause.Pause(); reached.TrySetResult(); }
            }), cancellation.Token, pause);
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var partial = Directory.GetFiles(work, "download.part", SearchOption.AllDirectories).Single();
            // The stream buffers writes, so inspect after cancellation/disposal below.
            await Task.Delay(100);
            Check(!download.IsCompleted, "pause holds resumable download open");
            cancellation.Cancel();
            var result = await download.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!result.Success && result.Error.Contains("Partial download saved"), "cancel while paused retains resume checkpoint");
            Check(new FileInfo(partial).Length > 0, "cancel flushes retained prefix before returning");
        }
        using var restart = new ResumableDownloadService();
        var complete = await restart.DownloadAsync(server.Url, work).WaitAsync(TimeSpan.FromSeconds(10));
        Check(complete.Success && (await File.ReadAllBytesAsync(complete.OutputPath)).SequenceEqual(Payload), "new downloader resumes paused/cancelled transfer exactly");
        Check(server.Requests[1].Range.Length > 0, "cancelled transfer resumes from its actual flushed length");
    }

    private static async Task CheckHeaderFailureAsync(string work)
    {
        await using var server = new Server("auth");
        using var downloader = new ResumableDownloadService();
        await downloader.DownloadAsync(server.Url, work);
        var partial = Directory.GetFiles(work, "download.part", SearchOption.AllDirectories).Single();
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(partial));
        var rejected = await downloader.DownloadAsync(server.Url, work);
        var retainedHash = SHA256.HashData(await File.ReadAllBytesAsync(partial));
        Check(!rejected.Success && rejected.Error.Contains("403") && hash.SequenceEqual(retainedHash),
            "HTTP rejection preserves valid cached bytes for a later retry");
        var result = await downloader.DownloadAsync(server.Url, work);
        Check(result.Success && (await File.ReadAllBytesAsync(result.OutputPath)).SequenceEqual(Payload), "retry after HTTP rejection resumes safely");
    }

    private static async Task CheckExclusiveAccessAsync(string work)
    {
        await using var server = new Server("slow");
        using var downloader = new ResumableDownloadService();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = downloader.DownloadAsync(server.Url, work, new InlineProgress(p => { if (p.Percentage > 0) reached.TrySetResult(); }), cancellation.Token);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = await downloader.DownloadAsync(server.Url, work).WaitAsync(TimeSpan.FromSeconds(5));
        Check(!duplicate.Success, "second app cannot open an active URL checkpoint");
        cancellation.Cancel();
        await first;
        Check(Directory.GetFiles(work, "download.part", SearchOption.AllDirectories).Length == 1, "failed second owner cannot delete first owner's partial");
    }

    private static async Task CheckPipelineRestartAsync(string root)
    {
        // Use an encrypted-output synthetic ISO/key: no decryptor or real game is needed.
        var marker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        var hadMarker = File.Exists(marker);
        if (!hadMarker) File.WriteAllText(marker, "Resume smoke");
        try
        {
            Directory.CreateDirectory(root);
            var keys = Path.Combine(root, "keys");
            Directory.CreateDirectory(keys);
            await File.WriteAllTextAsync(Path.Combine(keys, "Game café.dkey"), "00112233445566778899AABBCCDDEEFF");
            await using var server = new Server("slow");
            var metadata = new MetadataCacheService(new SettingsService());
            PipelineRunCoordinator CreatePipeline() => new(new KeyIndexService(), metadata, new QueuePreflightCoordinator(metadata));
            var settings = new PipelineRunSettings
            {
                RedumpKeysPath = keys, WorkDirectory = Path.Combine(root, "work"),
                IsoDestinationPath = Path.Combine(root, "output"), JbDestinationPath = Path.Combine(root, "jb"),
                RedrrxPs3DecPath = "unused.exe", SevenZipPath = "unused.exe", Ps3DecThreadCount = 1, RemovePs3Update = false
            };
            var pipeline = CreatePipeline();
            var job = new GameJob { SourcePath = server.Url, SourceType = GameSourceType.Url, OutputFormat = OutputFormat.EncryptedIsoWithKey };
            // Cancel from a progress notification on the UI-like caller context.
            var run = pipeline.RunAsync([job], settings);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (job.Stage != JobStage.Downloading || job.Progress <= 0)
            {
                if (run.IsCompleted || DateTime.UtcNow > deadline) throw new Exception("Pipeline did not begin writing.");
                await Task.Delay(10);
            }
            var queue = new QueueStateService(Path.Combine(root, "queue.json"));
            var shutdown = new QueueShutdownCoordinator(pipeline.Cancel, () => run, () => { queue.Save([job]); return Task.CompletedTask; });
            await shutdown.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(job.Stage == JobStage.Cancelled && job.LocalSourcePath is null, "pipeline shutdown settles cancelled transfer without setting a local source");
            Check(Directory.GetFiles(settings.WorkDirectory, "resume.json", SearchOption.AllDirectories).Length == 1, $"pipeline shutdown retains its checkpoint before queue flush ({job.Details})");
            var restored = queue.Load();
            await CreatePipeline().RunAsync(restored, settings).WaitAsync(TimeSpan.FromSeconds(15));
            var completed = restored.Single();
            Check(completed.State == JobState.Complete, $"restored pipeline resumes and publishes encrypted output ({completed.Details})");
            Check(completed.OutputPath is not null && (await File.ReadAllBytesAsync(completed.OutputPath)).SequenceEqual(Payload), "resumed pipeline publishes exact source bytes");
            Check(server.Requests[1].Range.Length > 0, "restored pipeline actually uses HTTP range");
            Check(!Directory.EnumerateFiles(settings.WorkDirectory, "*", SearchOption.AllDirectories).Any(), "completed pipeline removes partial, checkpoint and downloaded source");
            queue.Save(restored);
            Check(queue.Load().Count == 0, "completed resumed job does not return as pending work");
        }
        finally { if (!hadMarker) File.Delete(marker); }
    }

    private static byte[] MakePayload()
    {
        var bytes = new byte[2 * 1024 * 1024];
        new Random(42).NextBytes(bytes);
        return bytes;
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine($"PASS: {label}");
        _checks++;
    }

    private sealed class InlineProgress(Action<TransferProgress> report) : IProgress<TransferProgress>
    {
        public void Report(TransferProgress value) => report(value);
    }

    private sealed class Server : IAsyncDisposable
    {
        public const int PrefixLength = 128 * 1024;
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        public List<(string Range, string IfRange)> Requests { get; } = [];
        public byte[] ChangedPayload { get; } = Payload.Select(b => (byte)(b ^ 0xff)).ToArray();
        public long BytesSent { get; private set; }
        public string Url { get; }

        public Server(string mode)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/download";
            _serve = ServeAsync(mode);
        }

        private async Task ServeAsync(string mode)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    try { await RespondAsync(client, mode); }
                    catch (IOException) { /* Client cancelled an active transfer. */ }
                    catch (SocketException) { /* Client cancelled an active transfer. */ }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }

        private async Task RespondAsync(TcpClient client, string mode)
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await reader.ReadLineAsync(_stop.Token);
            while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
            {
                var colon = line.IndexOf(':');
                headers[line[..colon]] = line[(colon + 1)..].Trim();
            }
            var range = headers.GetValueOrDefault("Range", "");
            Requests.Add((range, headers.GetValueOrDefault("If-Range", "")));
            var number = Requests.Count;
            if ((mode == "auth" && number == 2) || (mode == "416" && number == 2))
            {
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {(mode == "auth" ? "403 Forbidden" : "416 Range Not Satisfiable")}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), _stop.Token);
                return;
            }
            var offset = range.Length > 0 ? int.Parse(range[6..^1]) : 0;
            var ranged = range.Length > 0 && mode is not ("ignored" or "changed");
            var payload = mode == "changed" && number > 1 ? ChangedPayload : Payload;
            var tag = mode == "no-tag" ? "" : mode == "weak" ? "ETag: W/\"fixture-v1\"\r\n" :
                mode is "changed" or "wrong-tag" && number == 2 ? "ETag: \"fixture-v2\"\r\n" : "ETag: \"fixture-v1\"\r\n";
            var start = ranged ? offset : 0;
            var contentRange = ranged ? $"Content-Range: bytes {(mode == "wrong-start" && number == 2 ? offset + 1 : offset)}-{Payload.Length - 1}/{(mode == "wrong-total" && number == 2 ? Payload.Length + 1 : Payload.Length)}\r\n" : "";
            var unknown = mode == "unknown-length" && number == 1;
            var length = unknown ? "Transfer-Encoding: chunked\r\n" : $"Content-Length: {payload.Length - start}\r\n";
            var fileName = mode == "percent" ? "Game%252520.iso" : "Game%20caf%C3%A9.iso";
            var response = $"HTTP/1.1 {(ranged ? "206 Partial Content" : "200 OK")}\r\n{length}{tag}{contentRange}Content-Disposition: attachment; filename*=UTF-8''{fileName}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
            if (number == 1 && mode != "slow")
            {
                if (unknown) await stream.WriteAsync(Encoding.ASCII.GetBytes($"{Payload.Length:x}\r\n"), _stop.Token);
                await stream.WriteAsync(payload.AsMemory(0, PrefixLength), _stop.Token);
                BytesSent += PrefixLength;
                return;
            }
            while (start < payload.Length)
            {
                var count = Math.Min(16 * 1024, payload.Length - start);
                await stream.WriteAsync(payload.AsMemory(start, count), _stop.Token);
                BytesSent += count;
                start += count;
                if (mode == "slow" && number == 1) await Task.Delay(15, _stop.Token);
            }
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
