using System.Collections.ObjectModel;
using PS3Butler.Models;
using PS3Butler.Services;

public static class PersistenceRestoreSmoke
{
    public static async Task RunAsync(string root)
    {
        var path = Path.Combine(root, "async-restore.json");
        var disk = new QueueStateService(path);
        disk.Save(new[]
        {
            new GameJob { SourcePath = "restore.iso", DestinationOverride = "custom", OutputFormat = OutputFormat.JbFolder },
            new GameJob { SourcePath = "https://example.invalid/restore.iso", SourceType = GameSourceType.Url }
        });
        using (var store = new ControlledLoadStore(disk))
        {
            var jobs = new ObservableCollection<GameJob>();
            var persistence = new QueuePersistenceCoordinator(store, jobs);
            var restoring = persistence.StartAsync();
            Require(!restoring.IsCompleted && jobs.Count == 0, "async restore returns before blocked disk read completes");
            Require(ReferenceEquals(restoring, persistence.StartAsync()), "repeated startup shares one restore task");
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stopping = persistence.StopAsync();
            Require(!stopping.IsCompleted && store.WriteCount == 0, "close during restore waits without saving empty queue");
            store.Release();
            await restoring.WaitAsync(TimeSpan.FromSeconds(5));
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Require(store.ReadCount == 1 && jobs.Count == 2 && jobs.All(job => job.State == JobState.Waiting), "restore applies inputs once as waiting jobs");
            Require(jobs[0].DestinationOverride == "custom" && jobs[0].OutputFormat == OutputFormat.JbFolder && jobs[1].SourceType == GameSourceType.Url,
                "async restore preserves meaningful input fields");
            Require(store.WriteCount == 1 && !store.WroteBeforeLoad && disk.Load().Count == 2, "shutdown flush follows restore and preserves unread inputs");
        }
        using (var store = new ControlledLoadStore(disk))
        {
            store.Release();
            var jobs = new ObservableCollection<GameJob>();
            var persistence = new QueuePersistenceCoordinator(store, jobs);
            await persistence.StartAsync();
            jobs[0].DestinationOverride = "edited after restore";
            await store.FirstWriteCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Require(disk.Load()[0].DestinationOverride == "edited after restore", "restored job subscriptions save later edits in background");
            await persistence.StopAsync();
        }
        using (var store = new ControlledLoadStore(disk) { FailLoad = true })
        {
            store.Release();
            var jobs = new ObservableCollection<GameJob>();
            var persistence = new QueuePersistenceCoordinator(store, jobs);
            var failed = false;
            try { await persistence.StartAsync(); } catch (IOException) { failed = true; }
            Require(failed, "unexpected restore failure remains observable");
            jobs.Add(new GameJob { SourcePath = "must-not-overwrite.iso" });
            try { await persistence.StopAsync(); } catch (IOException) { }
            Require(store.WriteCount == 0 && disk.Load().Count == 2, "failed restore never overwrites original queue during editing or shutdown");
        }
        File.WriteAllText(path, "invalid JSON");
        var corruptJobs = new ObservableCollection<GameJob>();
        var corrupt = new QueuePersistenceCoordinator(disk, corruptJobs);
        await corrupt.StartAsync();
        Require(corruptJobs.Count == 0, "corrupt queue retains safe empty-restore behavior asynchronously");
        await corrupt.StopAsync();
    }

    internal sealed class ControlledLoadStore(QueueStateService disk) : IQueueStateStore, IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private int _reads;
        private int _writes;
        private bool _loaded;
        public bool FailLoad { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstWriteCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ReadCount => Volatile.Read(ref _reads);
        public int WriteCount => Volatile.Read(ref _writes);
        public int ReadThreadId { get; private set; }
        public bool WroteBeforeLoad { get; private set; }

        public IReadOnlyList<GameJob> Load()
        {
            Interlocked.Increment(ref _reads);
            ReadThreadId = Environment.CurrentManagedThreadId;
            Started.TrySetResult();
            if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test load was not released.");
            if (FailLoad) throw new IOException("simulated restore failure");
            var jobs = disk.Load();
            _loaded = true;
            return jobs;
        }

        public void SaveSnapshot(IReadOnlyList<QueueStateService.PersistedGameJob> snapshot)
        {
            WroteBeforeLoad |= !_loaded;
            Interlocked.Increment(ref _writes);
            disk.SaveSnapshot(snapshot);
            FirstWriteCompleted.TrySetResult();
        }

        public void Release() => _release.Set();
        public void Dispose() { _release.Set(); _release.Dispose(); }
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception($"FAILED: {name}");
        Console.WriteLine($"PASS: {name}");
    }
}
