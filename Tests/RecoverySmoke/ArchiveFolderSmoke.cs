using System.Collections.ObjectModel;
using System.Security.Cryptography;
using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Services.Execution;
using PS3Butler.Services.Pipeline;

public static class ArchiveFolderSmoke
{
    public static async Task RunAsync(string toolsDirectory, string? decryptorPath)
    {
        var source = Directory.GetFiles(toolsDirectory, "*.iso").Single();
        var root = Path.GetFullPath(Path.Combine("artifacts", "archive-folder", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var archive = Path.Combine(root, Path.GetFileNameWithoutExtension(source) + ".zip");
        const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
        Console.WriteLine($"Test artifacts: {root}");
        var sourceHash = await HashAsync(source);
        var packed = await new ProcessRunner().RunAsync(new ProcessRunRequest
        {
            FileName = sevenZip,
            ArgumentList = new[] { "a", "-tzip", "-mx=0", archive, source },
            Timeout = TimeSpan.FromMinutes(2)
        });
        Require(packed.Success, "real ISO archived for extraction tests");

        var marker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        var hadMarker = File.Exists(marker);
        if (!hadMarker) File.WriteAllText(marker, "Archive/folder smoke runner");
        try
        {
            var metadata = new MetadataCacheService(new SettingsService());
            var preflight = new QueuePreflightCoordinator(metadata);
            var successful = Settings("archive-success");
            var completedJob = new GameJob { SourcePath = archive, OutputFormat = OutputFormat.EncryptedIsoWithKey };
            await Pipeline().RunAsync(new[] { completedJob }, successful);
            Require(completedJob.State == JobState.Complete, $"archive job completed: {completedJob.Details}");
            Require(completedJob.OutputPath is not null && await HashAsync(completedJob.OutputPath) == sourceHash, "archive output matches source ISO");
            Require(File.Exists(Path.ChangeExtension(completedJob.OutputPath, ".dkey")), "archive output publishes paired key");
            Require(!Directory.EnumerateFiles(successful.WorkDirectory, "*", SearchOption.AllDirectories).Any(), "completed archive workspace cleaned");

            await CancelScenario("archive-cancel", archive, OutputFormat.DecryptedIso,
                settings => HasFiles(Path.Combine(settings.WorkDirectory, "archive")));
            await CancelScenario("jb-extract-cancel", source, OutputFormat.JbFolder,
                settings => HasFiles(Path.Combine(settings.WorkDirectory, "jb")));
            await CancelScenario("jb-publish-cancel", source, OutputFormat.JbFolder,
                settings => Directory.EnumerateDirectories(settings.JbDestinationPath, "*.partial").Any());

            var raceSettings = Settings("jb-publish-race");
            var raceJob = new GameJob { SourcePath = source, OutputFormat = OutputFormat.JbFolder };
            var racePipeline = Pipeline();
            var race = racePipeline.RunAsync(new[] { raceJob }, raceSettings);
            string? finalDirectory = null;
            try
            {
                await WaitUntilAsync(() => Directory.EnumerateDirectories(raceSettings.JbDestinationPath, "*.partial").Any(), race);
                var staging = Directory.EnumerateDirectories(raceSettings.JbDestinationPath, "*.partial").Single();
                finalDirectory = staging[..staging.LastIndexOf(".ps3butler-", StringComparison.Ordinal)];
                Directory.CreateDirectory(finalDirectory);
                await File.WriteAllTextAsync(Path.Combine(finalDirectory, "existing.txt"), "Keep this existing folder");
            }
            finally
            {
                if (finalDirectory is null) racePipeline.Cancel();
                await race;
            }
            Require(raceJob.State == JobState.Error, "JB publish refuses destination created mid-copy");
            Require(await File.ReadAllTextAsync(Path.Combine(finalDirectory!, "existing.txt")) == "Keep this existing folder" &&
                Directory.EnumerateFileSystemEntries(finalDirectory!).Count() == 1, "existing JB destination remains untouched");
            Require(!Directory.EnumerateDirectories(raceSettings.JbDestinationPath, "*.partial").Any(), "failed JB publication staging cleaned");
            Require(!HasFiles(raceSettings.WorkDirectory), "failed JB publication workspace cleaned");
            Require(await HashAsync(source) == sourceHash, "source ISO unchanged after archive/JB tests");
            Console.WriteLine("Archive/JB interruption smoke checks passed.");

            PipelineRunCoordinator Pipeline() => new(new KeyIndexService(), metadata, preflight);

            async Task CancelScenario(string name, string input, OutputFormat format, Func<PipelineRunSettings, bool> active)
            {
                var settings = Settings(name);
                var jobs = new ObservableCollection<GameJob>();
                var store = new QueueStateService(Path.Combine(root, name + "-queue.json"));
                var persistence = new QueuePersistenceCoordinator(store, jobs);
                persistence.Start();
                var job = new GameJob { SourcePath = input, OutputFormat = format };
                jobs.Add(job);
                var pipeline = Pipeline();
                var running = pipeline.RunAsync(jobs, settings);
                try
                {
                    await WaitUntilAsync(() => active(settings), running);
                    Require(!running.IsCompleted, $"{name} reached active work");
                }
                finally
                {
                    await new QueueShutdownCoordinator(pipeline.Cancel, () => running, persistence.StopAsync).StopAsync();
                }
                Require(job.State == JobState.Error && job.Stage == JobStage.Cancelled, $"{name} settles cancelled: {job.Details}");
                var restored = store.Load();
                Require(restored.Count == 1 && restored[0].State == JobState.Waiting && restored[0].SourcePath == input && restored[0].OutputFormat == format,
                    $"{name} restores original queue input");
                Require(!HasFiles(settings.WorkDirectory), $"{name} workspace files cleaned");
                Require(!Directory.EnumerateFileSystemEntries(settings.IsoDestinationPath).Any() &&
                    !Directory.EnumerateFileSystemEntries(settings.JbDestinationPath).Any(), $"{name} leaves no final or staged output");
            }

            PipelineRunSettings Settings(string scenario)
            {
                var work = Path.Combine(root, scenario, "work");
                var iso = Path.Combine(root, scenario, "iso");
                var jb = Path.Combine(root, scenario, "jb");
                Directory.CreateDirectory(work);
                Directory.CreateDirectory(iso);
                Directory.CreateDirectory(jb);
                return new PipelineRunSettings
                {
                    RedrrxPs3DecPath = decryptorPath ?? Path.Combine(toolsDirectory, "ps3decrs.exe"),
                    SevenZipPath = sevenZip,
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

    private static bool HasFiles(string directory) => Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();

    private static async Task WaitUntilAsync(Func<bool> condition, Task operation)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (operation.IsCompleted || DateTime.UtcNow >= deadline)
                throw new Exception("Operation did not reach expected archive/JB stage.");
            await Task.Delay(1);
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
