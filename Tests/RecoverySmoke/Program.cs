using System.Collections.ObjectModel;
using Avalonia;
using PS3Butler.Models;
using PS3Butler.Services;

if (args.Length == 1 && args[0] == "--https")
{
    await HttpsDownloadSmoke.RunAsync();
    return 0;
}

if (args.Length == 1 && args[0] == "--archive-formats")
{
    await ArchiveFormatSmoke.RunAsync();
    return 0;
}

if (args.Length == 1 && args[0] == "--download-failures")
{
    await DownloadFailureSmoke.RunAsync();
    return 0;
}

if (args.Length == 1 && args[0] == "--preflight")
{
    await PreflightCancellationSmoke.RunAsync();
    return 0;
}

if ((args.Length == 2 || args.Length == 3) && args[0] == "--ordering")
{
    await PipelineOrderingSmoke.RunAsync(args[1], args.Length == 3 ? args[2] : null);
    return 0;
}

if ((args.Length == 4 || args.Length == 5) && args[0] == "--smb")
{
    await SmbSmoke.RunAsync(args[1], args[2], args[3], args.Length == 5 ? args[4] : null);
    return 0;
}

if ((args.Length == 2 || args.Length == 3) && args[0] == "--archive")
{
    await ArchiveFolderSmoke.RunAsync(args[1], args.Length == 3 ? args[2] : null);
    return 0;
}

if ((args.Length == 2 || args.Length == 3) && args[0] == "--window")
{
    MainWindowSmoke.Prepare(args[1], args.Length == 3 ? args[2] : null);
    return Avalonia.AppBuilder.Configure<MainWindowSmoke>().UsePlatformDetect()
        .StartWithClassicDesktopLifetime([]);
}

if (args.Length == 3 && args[0] == "--tools")
{
    await ToolCompatibilitySmoke.RunAsync(args[1], args[2]);
    return 0;
}

if (args.Contains("--native"))
{
    return Avalonia.AppBuilder.Configure<NativeShutdownSmoke>().UsePlatformDetect()
        .StartWithClassicDesktopLifetime([]);
}

if (args.Length == 2 && args[0] == "--real")
{
    await RealGameSmoke.RunAsync(args[1]);
    return 0;
}

if (args.Length == 2 && args[0] == "--interrupt")
{
    await InterruptionSmoke.RunAsync(args[1]);
    return 0;
}

var root = Path.Combine(Path.GetTempPath(), "PS3ButlerSmoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var store = new QueueStateService(Path.Combine(root, "queue.json"));
    var jobs = new ObservableCollection<GameJob>();
    var persistence = new QueuePersistenceCoordinator(store, jobs);
    await persistence.StartAsync();
    var job = new GameJob { SourcePath = "original.iso" };
    jobs.Add(job);
    job.DestinationOverride = "destination";
    jobs.Add(new GameJob { SourcePath = "complete.iso", State = JobState.Complete });
    jobs.Add(new GameJob { SourcePath = "existing.iso", State = JobState.AlreadyExists });
    await persistence.StopAsync();
    var restored = store.Load();
    Check(restored.Count == 1 && restored[0].DestinationOverride == "destination", "final snapshot and terminal filtering");
    Check(restored[0].State == JobState.Waiting && restored[0].LocalSourcePath is null, "fresh restored state");
    job.DestinationOverride = "after stop";
    Check(store.Load()[0].DestinationOverride == "destination", "stopped subscriptions");

    var snapshot = QueueStateService.CreateSnapshot(jobs);
    job.SourcePath = "changed.iso";
    store.SaveSnapshot(snapshot);
    Check(store.Load()[0].SourcePath == "original.iso", "detached snapshot");

    var blocked = Path.Combine(root, "blocked");
    File.WriteAllText(blocked, "not a directory");
    var failures = new List<string>();
    var failing = new QueuePersistenceCoordinator(new QueueStateService(Path.Combine(blocked, "queue.json")), jobs, failures.Add);
    await failing.StartAsync();
    var failed = false;
    try { await failing.StopAsync(); }
    catch (InvalidOperationException) { failed = true; }
    Check(failed && failures.Count > 0, "shutdown surfaces save failure");
    File.Delete(blocked);
    await failing.StopAsync();
    Check(File.Exists(Path.Combine(blocked, "queue.json")), "save retry after failure");

    File.WriteAllText(Path.Combine(root, "queue.json"), "corrupt");
    Check(store.Load().Count == 0, "corrupt queue safe restore");

    var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var cancelled = false;
    var flushed = false;
    var shutdown = new QueueShutdownCoordinator(() => cancelled = true, () => settled.Task, () => { flushed = true; return Task.CompletedTask; });
    var stopping = shutdown.StopAsync();
    Check(shutdown.IsStopping && cancelled && !flushed && !stopping.IsCompleted, "cancel before waiting and flushing");
    Check(ReferenceEquals(stopping, shutdown.StopAsync()), "repeated close shares shutdown");
    settled.SetResult();
    await stopping;
    Check(flushed, "flush after operations settle");

    var attempts = 0;
    var retry = new QueueShutdownCoordinator(() => { }, () => Task.CompletedTask, () => ++attempts == 1 ? Task.FromException(new IOException("save failed")) : Task.CompletedTask);
    try { await retry.StopAsync(); } catch (IOException) { }
    await retry.StopAsync();
    Check(attempts == 2, "shutdown retry");

    var finishingStore = new QueueStateService(Path.Combine(root, "finishing.json"));
    var finishingJob = new GameJob { SourcePath = "finishing.iso", State = JobState.Running };
    var finishingJobs = new ObservableCollection<GameJob> { finishingJob };
    var finishingPersistence = new QueuePersistenceCoordinator(finishingStore, finishingJobs);
    await finishingPersistence.StartAsync();
    var finishingOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var finishingShutdown = new QueueShutdownCoordinator(() => { }, () => finishingOperation.Task, finishingPersistence.StopAsync);
    var finishingStop = finishingShutdown.StopAsync();
    finishingJob.State = JobState.Complete;
    finishingOperation.SetResult();
    await finishingStop;
    Check(finishingStore.Load().Count == 0, "job completed during shutdown stays completed after restart");

    job.Stage = JobStage.Cancelled;
    Check(job.CurrentStage == "Cancelled", "typed cancellation display");
    job.CurrentStage = "Cancelled";
    Check(job.Stage == JobStage.Cancelled, "legacy cancellation bridge");

    var displaying = new GameJob { State = JobState.Running, Stage = JobStage.Copying };
    Check(displaying.Status == "Copying" && displaying.CurrentStage == "Copying ISO", "typed decrypted ISO copy preserves display labels");
    displaying.OutputFormat = OutputFormat.EncryptedIsoWithKey;
    Check(displaying.Stage == JobStage.Copying && displaying.Status == "Copying" && displaying.CurrentStage == "Copying encrypted ISO",
        "output format change refreshes copying label without changing typed stage");
    displaying.OutputFormat = OutputFormat.JbFolder;
    Check(displaying.Stage == JobStage.Copying && displaying.CurrentStage == "Copying JB folder", "typed JB copy preserves display label");
    displaying.Stage = JobStage.CreatingJbFolder;
    Check(displaying.State == JobState.Running && displaying.Status == "Extracting" && displaying.CurrentStage == "Creating JB folder",
        "typed JB extraction preserves status and specific stage label");
    displaying.CurrentStage = "Copying ISO";
    Check(displaying.Stage == JobStage.Copying && displaying.CurrentStage == "Copying ISO", "legacy copy stage bridge remains writable");
    displaying.Status = "Error";
    Check(displaying.State == JobState.Error, "legacy status bridge remains writable");
    await PersistenceCoalescingSmoke.RunAsync(root);
    await PersistenceRestoreSmoke.RunAsync(root);
    Console.WriteLine("Recovery smoke checks passed.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception($"FAILED: {name}");
    Console.WriteLine($"PASS: {name}");
}

return 0;
