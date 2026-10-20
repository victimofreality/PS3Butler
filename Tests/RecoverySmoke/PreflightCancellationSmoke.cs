using System.Net;
using System.Net.Sockets;
using System.Text;
using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Services.Execution;
using PS3Butler.Services.Pipeline;

public static class PreflightCancellationSmoke
{
    public static async Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "preflight", Guid.NewGuid().ToString("N")));
        var keys = Path.Combine(root, "keys");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(keys);
        Directory.CreateDirectory(destination);
        var source = Path.Combine(root, "Smoke Game.iso");
        var archive = Path.Combine(root, "Smoke Game.zip");
        File.WriteAllText(source, "preflight does not decrypt fixture inputs");
        File.WriteAllText(archive, "fixture inspected through injected runner");
        File.WriteAllText(Path.Combine(keys, "Smoke Game.dkey"), new string('0', 32));
        var marker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        var hadMarker = File.Exists(marker);
        if (!hadMarker) File.WriteAllText(marker, "Preflight smoke runner");
        try
        {
            var metadata = new MetadataCacheService(new SettingsService());
            var coordinator = new QueuePreflightCoordinator(metadata);
            var keyIndex = new KeyIndexService();
            keyIndex.Build(keys);
            using var alreadyCancelled = new CancellationTokenSource();
            alreadyCancelled.Cancel();
            await Cancelled(() => Task.Run(() => keyIndex.Build(keys, alreadyCancelled.Token)), "key index honours pre-cancelled token");
            Require(keyIndex.Count == 1, "pre-cancelled key build preserves previous index");
            await Cancelled(() => Task.Run(() => new DestinationInspectionService().CreateIndex(destination, alreadyCancelled.Token)), "destination index honours pre-cancelled token");
            var delayed = new DelayedRunner();
            var inspection = new ArchiveInspectionService(@"C:\Program Files\7-Zip\7z.exe", delayed);
            await Cancelled(() => inspection.FindIsoNameAsync(archive, alreadyCancelled.Token), "archive inspection honours pre-cancelled token");
            Require(!delayed.Started.Task.IsCompleted, "pre-cancelled inspection launches no process");

            var service = new GamePreflightService(keyIndex, inspection, new DestinationInspectionService(), metadata, destination, destination);
            var untouched = new GameJob { SourcePath = source, Details = "untouched" };
            await Cancelled(() => service.PreflightAsync(untouched, alreadyCancelled.Token), "job preflight honours pre-cancelled token");
            Require(untouched.State == JobState.Waiting && untouched.Details == "untouched", "pre-cancelled job remains untouched");
            using (var cancellation = new CancellationTokenSource())
            {
                var job = new GameJob { SourcePath = archive };
                var preflight = service.PreflightAsync(job, cancellation.Token);
                await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Require(job.Stage == JobStage.InspectingArchive, "archive preflight uses typed inspection stage");
                cancellation.Cancel();
                await Cancelled(() => preflight, "in-flight archive cancellation propagates");
                Require(delayed.Settled, "archive process runner settled before preflight returns");
                Require(job.Stage == JobStage.Cancelled && job.Details == "Preflight cancelled.", "archive cancellation is not reported as invalid archive");
                job.SourcePath = source;
                await service.PreflightAsync(job);
                Require(job.State == JobState.Ready && job.Stage == JobStage.None, "successful reinspection clears cancelled stage");
            }

            var listingArchive = Path.Combine(root, "real-listing.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(listingArchive, System.IO.Compression.ZipArchiveMode.Create))
            {
                zip.CreateEntry("Smoke Game.iso");
                for (var index = 0; index < 12000; index++) zip.CreateEntry($"listing/{index:D5}.txt");
            }
            using (var cancellation = new CancellationTokenSource())
            {
                var runner = new CancelOnOutputRunner(cancellation);
                var realInspection = new ArchiveInspectionService(@"C:\Program Files\7-Zip\7z.exe", runner);
                await Cancelled(() => realInspection.FindIsoNameAsync(listingArchive, cancellation.Token), "real 7-Zip inspection cancellation propagates");
                Require(runner.Result?.WasCancelled == true, "real listing process terminates and settles on cancellation");
            }

            using (var cancellation = new CancellationTokenSource())
            {
                var first = new GameJob { SourcePath = source };
                var next = new GameJob { SourcePath = source, Details = "untouched" };
                first.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GameJob.State) && first.State == JobState.Ready) cancellation.Cancel(); };
                await Cancelled(() => coordinator.PreflightQueueAsync(new[] { first, next }, keyIndex, keys, @"C:\Program Files\7-Zip\7z.exe",
                    destination, destination, new PreflightReservationSet(), cancellation.Token), "queue preflight stops on cancellation");
                Require(next.State == JobState.Waiting && next.Details == "untouched" && next.DetectedIsoName is null, "remaining queue jobs are not inspected");
            }

            var reserved = new GameJob { SourcePath = source };
            var duplicate = new GameJob { SourcePath = source };
            var overridden = new GameJob { SourcePath = source, DestinationOverride = Path.Combine(root, "override") };
            await coordinator.PreflightQueueAsync(new[] { reserved, duplicate, overridden }, keyIndex, keys, @"C:\Program Files\7-Zip\7z.exe",
                destination, destination, new PreflightReservationSet());
            Require(reserved.State == JobState.Ready && duplicate.State == JobState.AlreadyExists && overridden.State == JobState.Ready,
                "uncancelled queue reservations and override scope preserved");

            var settings = new PipelineRunSettings
            {
                RedrrxPs3DecPath = Path.Combine(root, "not-needed.exe"), SevenZipPath = @"C:\Program Files\7-Zip\7z.exe",
                RedumpKeysPath = keys, WorkDirectory = Path.Combine(root, "work"), IsoDestinationPath = destination,
                JbDestinationPath = destination, Ps3DecThreadCount = 4, RemovePs3Update = true
            };
            var pipeline = new PipelineRunCoordinator(keyIndex, metadata, coordinator);
            var active = new GameJob { SourcePath = archive };
            var waiting = new GameJob { SourcePath = source, Details = "untouched" };
            active.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GameJob.Stage) && active.Stage == JobStage.InspectingArchive) pipeline.Cancel(); };
            await pipeline.RunAsync(new[] { active, waiting }, settings);
            Require(active.Stage == JobStage.Cancelled && waiting.Details == "untouched", "initial pipeline preflight cancellation settles normally and skips remaining jobs");

            // Stop in the precise gap after a URL download, before its preflight.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var serving = ServeOnce(listener);
                var url = new GameJob { SourceType = GameSourceType.Url, SourcePath = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/Smoke%20Game.iso" };
                url.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(GameJob.Details) && url.Details == "Download complete. Preflighting...") pipeline.Cancel(); };
                await pipeline.RunAsync(new[] { url }, settings);
                await serving;
                Require(url.Stage == JobStage.Cancelled && url.LocalSourcePath is null, "post-download preflight cancellation settles and clears local path");
                Require(!Directory.EnumerateFiles(settings.WorkDirectory, "*", SearchOption.AllDirectories).Any(), "post-download preflight cancellation cleans downloaded file");
                Require(!Directory.EnumerateFileSystemEntries(destination).Any(), "preflight cancellation publishes nothing");
            }
            finally { listener.Stop(); }
            Console.WriteLine("Preflight cancellation smoke checks passed.");
        }
        finally { if (!hadMarker) File.Delete(marker); }
    }

    private static async Task ServeOnce(TcpListener listener)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = await listener.AcceptTcpClientAsync(timeout.Token);
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 }) { }
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: close\r\n\r\ntest"), timeout.Token);
    }

    private sealed class DelayedRunner : IProcessRunner
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Settled { get; private set; }
        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            Settled = true;
            return new ProcessRunResult { WasCancelled = true };
        }
    }

    private sealed class CancelOnOutputRunner(CancellationTokenSource cancellation) : IProcessRunner
    {
        public ProcessRunResult? Result { get; private set; }
        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Result = await new ProcessRunner().RunAsync(new ProcessRunRequest
            {
                FileName = request.FileName,
                Arguments = request.Arguments,
                Timeout = request.Timeout,
                StandardOutputChunkReceived = _ => cancellation.Cancel()
            }, cancellationToken);
            return Result;
        }
    }

    private static async Task Cancelled(Func<Task> operation, string name)
    {
        try { await operation().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { Console.WriteLine($"PASS: {name}"); return; }
        throw new Exception($"FAILED: {name}");
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception($"FAILED: {name}");
        Console.WriteLine($"PASS: {name}");
    }
}
