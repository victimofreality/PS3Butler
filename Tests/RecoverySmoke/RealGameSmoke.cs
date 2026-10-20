using System.Collections.ObjectModel;
using System.Security.Cryptography;
using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Services.Pipeline;

public static class RealGameSmoke
{
    public static async Task RunAsync(string toolsDirectory)
    {
        var source = Directory.GetFiles(toolsDirectory, "*.iso").Single();
        var id = "ButlerSmoke-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var workRoot = Path.GetFullPath(Path.Combine("artifacts", "real-game", id));
        Directory.CreateDirectory(workRoot);
        var marker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        var hadMarker = File.Exists(marker);
        if (!hadMarker) File.WriteAllText(marker, "Isolated smoke runner settings");
        try
        {
            var metadata = new MetadataCacheService(new SettingsService());
            var preflight = new QueuePreflightCoordinator(metadata);
            var initialHash = await HashAsync(source);
            foreach (var format in Enum.GetValues<OutputFormat>())
            {
                var settings = Settings(format.ToString());
                var job = new GameJob { SourcePath = source, OutputFormat = format };
                var pipeline = new PipelineRunCoordinator(new KeyIndexService(), metadata, preflight);
                Console.WriteLine($"RUN: {format}");
                await pipeline.RunAsync(new[] { job }, settings);
                Require(job.State == JobState.Complete, $"{format}: {job.Details}");
                Require(!string.IsNullOrWhiteSpace(job.OutputPath), "output path recorded");
                if (format == OutputFormat.JbFolder)
                {
                    Require(File.Exists(Path.Combine(job.OutputPath!, "PS3_GAME", "PARAM.SFO")), "JB metadata exists");
                    Require(!Directory.Exists(Path.Combine(job.OutputPath!, "PS3_UPDATE")), "optional update removal");
                }
                else
                {
                    Require(new FileInfo(job.OutputPath!).Length == new FileInfo(source).Length, "ISO output size");
                    if (format == OutputFormat.EncryptedIsoWithKey)
                    {
                        Require(await HashAsync(job.OutputPath!) == initialHash, "encrypted output matches source");
                        Require(File.Exists(Path.ChangeExtension(job.OutputPath, ".dkey")), "paired key exists");
                    }
                    else
                    {
                        Require(await HashAsync(job.OutputPath!) != initialHash, "decrypted output differs from encrypted source");
                    }
                }
                var duplicate = new GameJob { SourcePath = source, OutputFormat = format };
                await preflight.PreflightQueueAsync(new[] { duplicate }, new KeyIndexService(), settings.RedumpKeysPath,
                    settings.SevenZipPath, settings.IsoDestinationPath, settings.JbDestinationPath, new PreflightReservationSet());
                Require(duplicate.State == JobState.AlreadyExists, $"{format} duplicate detected: {duplicate.Details}");
                Require(!Directory.EnumerateFiles(settings.WorkDirectory, "*.iso", SearchOption.AllDirectories).Any(), "temporary ISOs cleaned");
                Console.WriteLine($"OUTPUT: {job.OutputPath}");
            }

            Console.WriteLine("RUN: shutdown during real decryption");
            var cancelSettings = Settings("cancel");
            var cancelJob = new GameJob { SourcePath = source, OutputFormat = OutputFormat.DecryptedIso };
            var jobs = new ObservableCollection<GameJob>();
            var queueStore = new QueueStateService(Path.Combine(workRoot, "cancel-queue.json"));
            var persistence = new QueuePersistenceCoordinator(queueStore, jobs);
            await persistence.StartAsync();
            jobs.Add(cancelJob);
            var cancelPipeline = new PipelineRunCoordinator(new KeyIndexService(), metadata, preflight);
            var running = cancelPipeline.RunAsync(jobs, cancelSettings);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (!running.IsCompleted && DateTime.UtcNow < deadline &&
                   !Directory.EnumerateFiles(cancelSettings.WorkDirectory, "*.iso", SearchOption.AllDirectories).Any())
                await Task.Delay(5);
            var interruptedActiveWork = !running.IsCompleted &&
                Directory.EnumerateFiles(cancelSettings.WorkDirectory, "*.iso", SearchOption.AllDirectories).Any();
            var shutdown = new QueueShutdownCoordinator(cancelPipeline.Cancel, () => running, persistence.StopAsync);
            await shutdown.StopAsync();
            Require(interruptedActiveWork, "cancel reached active decrypt output");
            Require(cancelJob.State == JobState.Error && cancelJob.Stage == JobStage.Cancelled, $"cancel state: {cancelJob.State}/{cancelJob.Stage}: {cancelJob.Details}");
            Require(queueStore.Load().Count == 1 && queueStore.Load()[0].State == JobState.Waiting, "cancelled job restores waiting");
            Require(!Directory.EnumerateFiles(cancelSettings.WorkDirectory, "*.iso", SearchOption.AllDirectories).Any(), "cancelled decrypt output cleaned");
            Require(!Directory.EnumerateFileSystemEntries(cancelSettings.IsoDestinationPath).Any(), "cancelled job publishes nothing");
            Require(await HashAsync(source) == initialHash, "source ISO unchanged after all tests");
            Console.WriteLine($"Real-game smoke checks passed. Test ID: {id}");

            PipelineRunSettings Settings(string scenario)
            {
                var iso = Path.Combine(toolsDirectory, "ISO", id, scenario);
                var jb = Path.Combine(toolsDirectory, "Games", id, scenario);
                var work = Path.Combine(workRoot, scenario);
                Directory.CreateDirectory(iso);
                Directory.CreateDirectory(jb);
                Directory.CreateDirectory(work);
                return new PipelineRunSettings
                {
                    RedrrxPs3DecPath = Path.Combine(toolsDirectory, "ps3decrs.exe"),
                    SevenZipPath = @"C:\Program Files\7-Zip\7z.exe",
                    RedumpKeysPath = Path.Combine(toolsDirectory, "Keys"),
                    WorkDirectory = work,
                    IsoDestinationPath = iso,
                    JbDestinationPath = jb,
                    Ps3DecThreadCount = 4,
                    RemovePs3Update = true
                };
            }
        }
        finally
        {
            if (!hadMarker) File.Delete(marker);
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
