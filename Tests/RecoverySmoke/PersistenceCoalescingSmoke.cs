using System.Collections.ObjectModel;
using PS3Butler.Models;
using PS3Butler.Services;

public static class PersistenceCoalescingSmoke
{
    public static async Task RunAsync(string root)
    {
        using (var store = new ControlledStore(Path.Combine(root, "burst.json")))
        {
            var jobs = new ObservableCollection<GameJob>();
            var persistence = new QueuePersistenceCoordinator(store, jobs);
            persistence.Start();
            var job = new GameJob { SourcePath = "burst.iso", DestinationOverride = "before" };
            jobs.Add(job);
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var index = 0; index < 1000; index++) job.DestinationOverride = "edit-" + index;
            job.OutputFormat = OutputFormat.JbFolder;
            jobs.Add(new GameJob { SourcePath = "completed.iso", State = JobState.Complete });
            jobs.Add(new GameJob { SourcePath = "exists.iso", State = JobState.AlreadyExists });
            jobs.Add(new GameJob { SourcePath = "https://example.invalid/pending.iso", SourceType = GameSourceType.Url });
            var stopping = persistence.StopAsync();
            Require(!stopping.IsCompleted, "shutdown waits for blocked active queue write");
            job.DestinationOverride = "after stop";
            store.Release();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Require(store.WriteCount == 2 && store.MaxActive == 1, "1000 edits coalesce into one latest write after active write");
            Require(store.FirstSnapshot![0].DestinationOverride == "before", "active write uses detached snapshot");
            var restored = store.Load();
            Require(restored.Count == 2 && restored[0].DestinationOverride == "edit-999" && restored[0].OutputFormat == OutputFormat.JbFolder,
                "final snapshot contains latest meaningful inputs and ignores edits after stop");
            Require(restored.All(job => job.State == JobState.Waiting) && restored[1].SourceType == GameSourceType.Url,
                "coalesced save omits terminal jobs and restores URL input waiting");
        }

        using (var store = new ControlledStore(Path.Combine(root, "recover-write.json")) { FailWriteNumber = 1 })
        {
            var messages = new List<string>();
            var jobs = new ObservableCollection<GameJob>();
            var persistence = new QueuePersistenceCoordinator(store, jobs, messages.Add);
            persistence.Start();
            var job = new GameJob { SourcePath = "recover.iso", DestinationOverride = "before" };
            jobs.Add(job);
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            job.DestinationOverride = "latest";
            var stopping = persistence.StopAsync();
            store.Release();
            await stopping.WaitAsync(TimeSpan.FromSeconds(5));
            Require(messages.Count == 1 && messages[0].Contains("simulated write failure"), "intermediate save failure is reported");
            Require(store.WriteCount == 2 && store.Load()[0].DestinationOverride == "latest", "newest snapshot saves after previous write failure");
        }

        using (var store = new ControlledStore(Path.Combine(root, "final-failure.json")) { FailWriteNumber = 2 })
        {
            var messages = new List<string>();
            var jobs = new ObservableCollection<GameJob>();
            var persistence = new QueuePersistenceCoordinator(store, jobs, messages.Add);
            persistence.Start();
            var job = new GameJob { SourcePath = "retry.iso", DestinationOverride = "before" };
            jobs.Add(job);
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            job.DestinationOverride = "latest";
            var stopping = persistence.StopAsync();
            store.Release();
            var failed = false;
            try { await stopping.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (InvalidOperationException) { failed = true; }
            Require(failed && messages.Count == 1, "latest coalesced save failure blocks shutdown");
            Require(store.Load()[0].DestinationOverride == "before", "failed final write retains previous saved queue");
            await persistence.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Require(store.WriteCount == 3 && store.MaxActive == 1 && store.Load()[0].DestinationOverride == "latest",
                "shutdown retry saves newest inputs without concurrent writes");
        }
    }

    private sealed class ControlledStore(string path) : IQueueStateStore, IDisposable
    {
        private readonly QueueStateService _disk = new(path);
        private readonly ManualResetEventSlim _release = new(false);
        private int _writes;
        private int _active;
        public int FailWriteNumber { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<QueueStateService.PersistedGameJob>? FirstSnapshot { get; private set; }
        public int WriteCount => Volatile.Read(ref _writes);
        public int MaxActive { get; private set; }

        public IReadOnlyList<GameJob> Load() => _disk.Load();

        public void SaveSnapshot(IReadOnlyList<QueueStateService.PersistedGameJob> snapshot)
        {
            var number = Interlocked.Increment(ref _writes);
            MaxActive = Math.Max(MaxActive, Interlocked.Increment(ref _active));
            try
            {
                if (number == 1)
                {
                    FirstSnapshot = snapshot;
                    Started.TrySetResult();
                    if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test write was not released.");
                }
                if (number == FailWriteNumber) throw new IOException("simulated write failure");
                _disk.SaveSnapshot(snapshot);
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _release.Dispose();
        }
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception($"FAILED: {name}");
        Console.WriteLine($"PASS: {name}");
    }
}
