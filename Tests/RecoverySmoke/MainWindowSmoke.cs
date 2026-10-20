using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using PS3Butler.Configuration;
using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.ViewModels;
using PS3Butler.Views;

public sealed class MainWindowSmoke : Application
{
    private static string _root = string.Empty;
    private static string _source = string.Empty;

    public static void Prepare(string toolsDirectory, string? decryptorPath = null)
    {
        _source = Directory.GetFiles(toolsDirectory, "*.iso").Single();
        _root = Path.Combine(AppContext.BaseDirectory, "work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        // Run from an isolated output folder: portable settings must never share
        // the normal app's settings or queue.
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "portable.txt"), "MainWindow smoke runner");
        var settings = new SettingsService();
        settings.Save(new AppSettings
        {
            RedrrxPs3DecPath = decryptorPath ?? Path.Combine(toolsDirectory, "ps3decrs.exe"),
            SevenZipPath = @"C:\Program Files\7-Zip\7z.exe",
            RedumpKeysPath = Path.Combine(toolsDirectory, "Keys"),
            WorkDirectory = _root,
            IsoDestinationPath = Path.Combine(_root, "iso"),
            JbDestinationPath = Path.Combine(_root, "jb"),
            Ps3DecThreadCount = 4
        });
        new QueueStateService(settings).Save(new[] { new GameJob { SourcePath = _source } });
    }

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        // Keep the dispatcher running while real MainWindow instances close and
        // reopen, just as separate app launches would restore their queue.
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await RunAsync();
                Console.WriteLine("MainWindow integration smoke checks passed.");
                desktop.Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                // Settle real work even if a test assertion failed.
                foreach (var window in desktop.Windows.ToArray())
                    if (window.DataContext is MainViewModel viewModel)
                        await viewModel.StopAsync();
                desktop.Shutdown(1);
            }
        });
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunAsync()
    {
        var window = CreateWindow();
        var closed = ClosedTask(window);
        var viewModel = (MainViewModel)window.DataContext!;
        Require(viewModel.Jobs.Count == 1 && viewModel.Jobs[0].State == JobState.Waiting, "real window restores pending queue");
        Require(viewModel.SettingsMode == "Portable" && viewModel.WorkDirectory == _root, "real window uses isolated settings");
        await viewModel.SaveSettingsCommand.ExecuteAsync(null);
        Require(viewModel.SettingsStatus == "Settings saved." && new SettingsService().Load().RedrrxPs3DecPath == viewModel.RedrrxPs3DecPath,
            "settings validation accepts and preserves selected Redrrx binary");
        var run = viewModel.ProcessReadyJobsCommand.ExecuteAsync(null);
        Require(viewModel.IsPipelineActive && !viewModel.CanEditQueue, "processing command locks queue");
        await WaitUntilAsync(() => Directory.EnumerateFiles(_root, "*.decrypted.iso*").Any(), run);
        Require(!run.IsCompleted, "window test reached active decrypt process");
        viewModel.SelectedJob = viewModel.Jobs[0];
        window.Close();
        window.Close();
        Require(!closed.IsCompleted && !viewModel.CanEditQueue, "native close waits and keeps queue locked");
        viewModel.UrlToAdd = "https://example.invalid/blocked.iso";
        viewModel.AddUrlCommand.Execute(null);
        viewModel.RemoveSelectedJobCommand.Execute(null);
        viewModel.ClearQueueCommand.Execute(null);
        await viewModel.PreflightQueueCommand.ExecuteAsync(null);
        Require(viewModel.Jobs.Count == 1 && viewModel.Jobs[0].SourcePath == _source, "queue commands cannot edit during shutdown");
        await run.WaitAsync(TimeSpan.FromSeconds(10));
        await closed.WaitAsync(TimeSpan.FromSeconds(10));
        Require(!viewModel.IsPipelineActive && !viewModel.CanEditQueue, "settled pipeline does not unlock stopping queue");
        Require(viewModel.Jobs[0].Stage == JobStage.Cancelled, "MainWindow shutdown tracks cancelled stage");
        Require(!Directory.EnumerateFiles(_root, "*.iso*", SearchOption.AllDirectories).Any(), "window shutdown cleans partial output and recovery files");

        var store = new QueueStateService(new SettingsService());
        Require(store.Load().Count == 1 && store.Load()[0].State == JobState.Waiting, "queue flushed before native window closed");
        var restoredWindow = CreateWindow();
        var restored = (MainViewModel)restoredWindow.DataContext!;
        Require(restored.Jobs.Count == 1 && restored.Jobs[0].SourcePath == _source && restored.Jobs[0].State == JobState.Waiting &&
            restored.Jobs[0].LocalSourcePath is null, "reopened MainWindow restores fresh waiting job");
        var restoredClosed = ClosedTask(restoredWindow);
        restoredWindow.Close();
        await restoredClosed.WaitAsync(TimeSpan.FromSeconds(10));
        Require(restoredClosed.IsCompleted, "idle restored window closes after flush");

        var preflightWindow = CreateWindow();
        var preflightViewModel = (MainViewModel)preflightWindow.DataContext!;
        var firstPreflight = preflightViewModel.Jobs[0];
        var untouched = new GameJob { SourcePath = _source, Details = "untouched" };
        preflightViewModel.Jobs.Add(untouched);
        System.ComponentModel.PropertyChangedEventHandler cancelPreflight = (_, e) =>
        {
            if (e.PropertyName == nameof(GameJob.State) && firstPreflight.State == JobState.Ready)
                preflightViewModel.CancelDownloadCommand.Execute(null);
        };
        firstPreflight.PropertyChanged += cancelPreflight;
        var inspecting = preflightViewModel.PreflightQueueCommand.ExecuteAsync(null);
        Require(!preflightViewModel.CanEditQueue, "standalone preflight locks queue");
        preflightViewModel.UrlToAdd = "https://example.invalid/blocked.iso";
        preflightViewModel.AddUrlCommand.Execute(null);
        await preflightViewModel.ProcessReadyJobsCommand.ExecuteAsync(null);
        Require(preflightViewModel.Jobs.Count == 2 && !preflightViewModel.IsPipelineActive, "preflight blocks queue additions and concurrent pipeline start");
        await inspecting.WaitAsync(TimeSpan.FromSeconds(10));
        firstPreflight.PropertyChanged -= cancelPreflight;
        Require(preflightViewModel.SettingsStatus == "Preflight cancelled." && preflightViewModel.CanEditQueue,
            "standalone cancellation reports status and unlocks queue after settling");
        Require(firstPreflight.Stage == JobStage.Cancelled && untouched.State == JobState.Waiting && untouched.Details == "untouched",
            "standalone cancellation skips remaining queue jobs");
        preflightViewModel.SelectedJob = firstPreflight;
        await preflightViewModel.RetrySelectedJobCommand.ExecuteAsync(null);
        Require(firstPreflight.State == JobState.Ready && untouched.State == JobState.AlreadyExists,
            "preflight retry succeeds and preserves duplicate reservations");
        var preflightClosed = ClosedTask(preflightWindow);
        preflightWindow.Close();
        await preflightClosed.WaitAsync(TimeSpan.FromSeconds(10));

        var archivePath = Path.Combine(_root, "inspection.zip");
        using (var archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create))
            archive.CreateEntry(Path.GetFileName(_source));
        var closingPreflightWindow = CreateWindow();
        var closingPreflight = (MainViewModel)closingPreflightWindow.DataContext!;
        closingPreflight.Jobs.Clear();
        var archiveJob = new GameJob { SourcePath = archivePath };
        var laterJob = new GameJob { SourcePath = _source, Details = "untouched" };
        closingPreflight.Jobs.Add(archiveJob);
        closingPreflight.Jobs.Add(laterJob);
        var inspectionClosed = ClosedTask(closingPreflightWindow);
        archiveJob.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameJob.Stage) && archiveJob.Stage == JobStage.InspectingArchive)
                closingPreflightWindow.Close();
        };
        await closingPreflight.PreflightQueueCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(10));
        await inspectionClosed.WaitAsync(TimeSpan.FromSeconds(10));
        Require(archiveJob.Stage == JobStage.Cancelled && laterJob.Details == "untouched", "window close cancels archive preflight before remaining jobs");
        Require(!closingPreflight.CanEditQueue && store.Load().Count == 2 && store.Load().All(job => job.State == JobState.Waiting),
            "window close waits for preflight then flushes both queue inputs");
        // Restore the single-input fixture for the existing save-failure test.
        store.Save(new[] { new GameJob { SourcePath = _source } });

        var failureWindow = CreateWindow();
        var failureViewModel = (MainViewModel)failureWindow.DataContext!;
        var failureClosed = ClosedTask(failureWindow);
        var queuePath = new SettingsService().QueueFilePath;
        using (var lockedQueue = new FileStream(queuePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            failureWindow.Close();
            await WaitUntilAsync(() => failureViewModel.SettingsStatus.StartsWith("Shutdown failed:", StringComparison.Ordinal));
            Require(!failureClosed.IsCompleted && !failureViewModel.CanEditQueue, "actual queue-save failure blocks native close and edits");
            Require(failureViewModel.SettingsStatus.Contains("Close again to retry"), "save failure presents retry instruction");
        }
        failureWindow.Close();
        await failureClosed.WaitAsync(TimeSpan.FromSeconds(10));
        Require(store.Load().Count == 1, "retry saves queue and closes actual window");
    }

    private static MainWindow CreateWindow()
    {
        var window = new MainWindow { ShowInTaskbar = false, Opacity = 0 };
        window.Show();
        return window;
    }

    private static Task ClosedTask(Window window)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => completion.TrySetResult();
        return completion.Task;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, Task? operation = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition())
        {
            if (operation?.IsCompleted == true || DateTime.UtcNow >= deadline)
                throw new Exception("Expected MainWindow operation did not become active.");
            await Task.Delay(1);
        }
    }

    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception($"FAILED: {name}");
        Console.WriteLine($"PASS: {name}");
    }
}
