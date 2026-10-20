using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Services.Pipeline;
using PS3Butler.Services.Storage;

public static class SmbSmoke
{
    public static async Task RunAsync(string toolsDirectory, string isoParent, string jbParent, string? decryptor)
    {
        isoParent = Normalize(isoParent);
        jbParent = Normalize(jbParent);
        if (!isoParent.StartsWith(@"\\") || !jbParent.StartsWith(@"\\") ||
            string.Equals(isoParent, jbParent, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Provide distinct existing UNC destination folders.");
        var isoBefore = Snapshot(isoParent);
        var jbBefore = Snapshot(jbParent);
        var id = Guid.NewGuid().ToString("N");
        var leaf = ".ps3butler-smoke-" + id;
        var isoRoot = Path.Combine(isoParent, leaf);
        var jbRoot = Path.Combine(jbParent, leaf);
        var local = Path.GetFullPath(Path.Combine("artifacts", "smb", id));
        Directory.CreateDirectory(local);
        // Keep exact cleanup targets and ownership token outside the share.
        await File.WriteAllTextAsync(Path.Combine(local, "manifest.json"), JsonSerializer.Serialize(new { isoRoot, jbRoot, id }));
        var marker = Path.Combine(AppContext.BaseDirectory, "portable.txt");
        var hadMarker = File.Exists(marker);
        var owned = new List<(string Parent, string Path)>();
        try
        {
            CreateOwned(isoParent, isoRoot);
            CreateOwned(jbParent, jbRoot);
            if (!hadMarker) File.WriteAllText(marker, "SMB smoke runner");
            var source = Directory.GetFiles(toolsDirectory, "*.iso").Single();
            var sourceSize = new FileInfo(source).Length;
            var sourceHash = await HashAsync(source);
            foreach (var destination in new[] { isoRoot, jbRoot })
            {
                var budget = await new DiskBudgetService().CheckAsync(destination, sourceSize, 2L * 1024 * 1024 * 1024);
                Require(budget.IsSufficient && budget.AvailableBytes > 0, $"UNC free-space check: {destination}: {budget.Message}");
                var insufficient = await new DiskBudgetService().CheckAsync(destination, long.MaxValue);
                Require(!insufficient.IsSufficient && insufficient.AvailableBytes > 0, "UNC budget rejects impossible requirement");
            }
            var metadata = new MetadataCacheService(new SettingsService());
            var preflight = new QueuePreflightCoordinator(metadata);
            string? publishedJb = null;
            foreach (var format in Enum.GetValues<OutputFormat>())
            {
                Console.WriteLine($"RUN SMB: {format}");
                var settings = Settings(format.ToString());
                var job = new GameJob { SourcePath = source, OutputFormat = format };
                await Pipeline().RunAsync(new[] { job }, settings);
                Require(job.State == JobState.Complete, $"SMB {format} completes: {job.Details}");
                if (format == OutputFormat.JbFolder)
                {
                    publishedJb = job.OutputPath!;
                    var game = new ParamSfoService().Read(Path.Combine(publishedJb, "PS3_GAME", "PARAM.SFO"));
                    Require(!string.IsNullOrWhiteSpace(game?.TitleId) && Path.GetFileName(publishedJb).Contains(game.TitleId), "SMB JB metadata and title-ID naming");
                    Require(!Directory.Exists(Path.Combine(publishedJb, "PS3_UPDATE")), "SMB optional update removal");
                }
                else
                {
                    Require(new FileInfo(job.OutputPath!).Length == sourceSize, "SMB ISO output length matches source");
                    var outputHash = await HashAsync(job.OutputPath!);
                    Require(format == OutputFormat.EncryptedIsoWithKey ? outputHash == sourceHash : outputHash != sourceHash,
                        $"SMB {format} output hash validated");
                    if (format == OutputFormat.EncryptedIsoWithKey)
                        Require(KeyIndexService.TryReadKey(Path.ChangeExtension(job.OutputPath!, ".dkey"), out var key, out _) && key == job.DiscKey,
                            "SMB encrypted output key matches");
                }
                var duplicate = new GameJob { SourcePath = source, OutputFormat = format };
                await preflight.PreflightQueueAsync(new[] { duplicate }, new KeyIndexService(), settings.RedumpKeysPath,
                    settings.SevenZipPath, settings.IsoDestinationPath, settings.JbDestinationPath, new PreflightReservationSet());
                Require(duplicate.State == JobState.AlreadyExists, $"SMB {format} duplicate detected");
                Require(!HasFiles(settings.WorkDirectory), $"SMB {format} local workspace cleaned");
            }

            foreach (var format in Enum.GetValues<OutputFormat>())
            {
                Console.WriteLine($"RUN SMB cancellation: {format}");
                var settings = Settings("cancel-" + format);
                var jobs = new ObservableCollection<GameJob>();
                var store = new QueueStateService(Path.Combine(local, format + "-queue.json"));
                var persistence = new QueuePersistenceCoordinator(store, jobs);
                persistence.Start();
                var job = new GameJob { SourcePath = source, OutputFormat = format };
                jobs.Add(job);
                var pipeline = Pipeline();
                var running = pipeline.RunAsync(jobs, settings);
                try
                {
                    var destination = format == OutputFormat.JbFolder ? settings.JbDestinationPath : settings.IsoDestinationPath;
                    var deadline = DateTime.UtcNow.AddSeconds(45);
                    while (!Directory.EnumerateFileSystemEntries(destination, "*.partial").Any())
                    {
                        if (running.IsCompleted || DateTime.UtcNow >= deadline)
                            throw new Exception($"SMB {format} did not reach network staging.");
                        await Task.Delay(20);
                    }
                    Require(!running.IsCompleted, $"SMB {format} cancellation reaches active network copy");
                }
                finally
                {
                    await new QueueShutdownCoordinator(pipeline.Cancel, () => running, persistence.StopAsync).StopAsync();
                }
                Require(job.Stage == JobStage.Cancelled, $"SMB {format} cancellation settles");
                Require(!Directory.EnumerateFileSystemEntries(settings.IsoDestinationPath).Any() &&
                    !Directory.EnumerateFileSystemEntries(settings.JbDestinationPath).Any(), $"SMB {format} cancellation leaves no final or staged output");
                Require(!HasFiles(settings.WorkDirectory), $"SMB {format} cancelled workspace cleaned");
                var restored = store.Load();
                Require(restored.Count == 1 && restored[0].State == JobState.Waiting && restored[0].SourcePath == source,
                    $"SMB {format} cancelled job restores waiting");
            }

            var protectedSettings = Settings("no-overwrite");
            var protectedIso = Path.Combine(protectedSettings.IsoDestinationPath, "existing.iso");
            await File.WriteAllTextAsync(protectedIso, "Keep existing ISO");
            var rejectedIso = await new DeliveryService().CopyAsync(source, protectedSettings.IsoDestinationPath, destinationFileName: "existing.iso");
            Require(!rejectedIso.Success && await File.ReadAllTextAsync(protectedIso) == "Keep existing ISO", "SMB existing ISO is not overwritten");
            var protectedJb = Path.Combine(protectedSettings.JbDestinationPath, "existing");
            Directory.CreateDirectory(protectedJb);
            await File.WriteAllTextAsync(Path.Combine(protectedJb, "existing.txt"), "Keep existing JB");
            var rejectedJb = await new FolderDeliveryService().CopyAsync(publishedJb!, protectedJb);
            Require(!rejectedJb.Success && await File.ReadAllTextAsync(Path.Combine(protectedJb, "existing.txt")) == "Keep existing JB",
                "SMB existing JB folder is not merged or overwritten");
            Require(await HashAsync(source) == sourceHash, "SMB tests leave source ISO unchanged");

            PipelineRunCoordinator Pipeline() => new(new KeyIndexService(), metadata, preflight);

            PipelineRunSettings Settings(string scenario)
            {
                var work = Path.Combine(local, scenario);
                var iso = Path.Combine(isoRoot, scenario);
                var jb = Path.Combine(jbRoot, scenario);
                Directory.CreateDirectory(work);
                Directory.CreateDirectory(iso);
                Directory.CreateDirectory(jb);
                return new PipelineRunSettings
                {
                    RedrrxPs3DecPath = decryptor ?? Path.Combine(toolsDirectory, "ps3decrs.exe"),
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
            var cleanupErrors = new List<Exception>();
            foreach (var target in owned)
            {
                try
                {
                    ValidateChild(target.Parent, target.Path, leaf);
                    if (File.ReadAllText(Path.Combine(target.Path, ".butler-smoke-owner")) != id)
                        throw new IOException($"Ownership marker mismatch; refusing cleanup: {target.Path}");
                    RejectReparsePoints(target.Path);
                    Directory.Delete(target.Path, recursive: true);
                    Require(!Directory.Exists(target.Path), $"SMB test container removed: {target.Path}");
                }
                catch (Exception ex) { cleanupErrors.Add(ex); }
            }
            if (!hadMarker && File.Exists(marker)) File.Delete(marker);
            if (cleanupErrors.Count > 0)
                throw new AggregateException($"SMB cleanup incomplete; exact targets are in {Path.Combine(local, "manifest.json")}", cleanupErrors);
            Require(isoBefore.SequenceEqual(Snapshot(isoParent)), "production PS3ISO top-level entries unchanged after cleanup");
            Require(jbBefore.SequenceEqual(Snapshot(jbParent)), "production GAMES top-level entries unchanged after cleanup");
        }
        Console.WriteLine("SMB smoke checks passed; remote test containers removed.");

        void CreateOwned(string parent, string path)
        {
            ValidateChild(parent, path, leaf);
            if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Test container already exists.");
            Directory.CreateDirectory(path);
            try
            {
                using var file = new StreamWriter(new FileStream(Path.Combine(path, ".butler-smoke-owner"), FileMode.CreateNew));
                file.Write(id);
            }
            catch
            {
                // Only the new empty container is eligible for non-recursive removal.
                Directory.Delete(path, recursive: false);
                throw;
            }
            owned.Add((parent, path));
            Console.WriteLine($"OWNED SMB TEST CONTAINER: {path}");
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void ValidateChild(string parent, string path, string leaf)
    {
        if (!string.Equals(Path.GetDirectoryName(Normalize(path)), Normalize(parent), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(path), leaf, StringComparison.Ordinal) || !leaf.StartsWith(".ps3butler-smoke-", StringComparison.Ordinal))
            throw new IOException($"Refusing unsafe test target: {path}");
    }

    private static void RejectReparsePoints(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Refusing cleanup of a reparse point: {path}");
        if (Directory.Exists(path))
            foreach (var child in Directory.EnumerateFileSystemEntries(path)) RejectReparsePoints(child);
    }

    private static string[] Snapshot(string path) => new DirectoryInfo(path).EnumerateFileSystemInfos()
        .Select(item => $"{item.Name}|{item.Attributes}|{item.LastWriteTimeUtc.Ticks}|{(item is FileInfo file ? file.Length : 0)}")
        .OrderBy(item => item, StringComparer.Ordinal).ToArray();

    private static bool HasFiles(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any();

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
