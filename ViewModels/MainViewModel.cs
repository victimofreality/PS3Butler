using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PS3Butler.Configuration;
using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Services.Pipeline;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace PS3Butler.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly Window _window;
    private readonly KeyIndexService _keyIndexService;
    private readonly MetadataCacheService _metadataCacheService;
    private readonly QueuePreflightCoordinator _preflightCoordinator;
    private readonly QueueStateService _queueStateService;
    private readonly QueuePersistenceCoordinator _queuePersistence;
    private CancellationTokenSource? _pipelineCancellationTokenSource;
    private CancellationTokenSource? _downloadCancellationTokenSource;
    private PauseTokenSource? _downloadPauseTokenSource;

    public string Greeting { get; } = "PS3 Butler";

    public ObservableCollection<ToolStatus> Tools { get; } = [];
    public ObservableCollection<GameJob> Jobs { get; } = [];

    [ObservableProperty]
    public partial GameJob? SelectedJob { get; set; }

    [ObservableProperty]
    public partial string RedrrxPs3DecPath { get; set; }

    [ObservableProperty]
    public partial string SevenZipPath { get; set; }

    [ObservableProperty]
    public partial string RedumpKeysPath { get; set; }

    [ObservableProperty]
    public partial int Ps3DecThreadCount { get; set; }

    [ObservableProperty]
    public partial string WorkDirectory { get; set; }

    [ObservableProperty]
    public partial string IsoDestinationPath { get; set; }

    [ObservableProperty]
    public partial string JbDestinationPath { get; set; }
    [ObservableProperty]
    public partial string SettingsStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RemovePs3Update { get; set; }

    [ObservableProperty]
    public partial string UrlToAdd { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPipelineActive { get; set; }

    public bool CanEditQueue =>
    !IsPipelineActive;

    partial void OnIsPipelineActiveChanged(
        bool value)
    {
        OnPropertyChanged(
            nameof(CanEditQueue));
    }

    public string SettingsLocation => _settingsService.SettingsFilePath;

    public string SettingsMode =>
        _settingsService.IsPortableMode ? "Portable" : "Normal";

    public MainViewModel(Window window)
    {
        _window = window;

        _settingsService =
            new SettingsService();

        _metadataCacheService =
            new MetadataCacheService(
                _settingsService);

        _preflightCoordinator =
            new QueuePreflightCoordinator(
                _metadataCacheService);

        _queueStateService =
            new QueueStateService(
                _settingsService);

        _queuePersistence =
            new QueuePersistenceCoordinator(
                _queueStateService,
                Jobs,
                message =>
                    SettingsStatus = message);

        _settings =
            _settingsService.Load();

        _keyIndexService =
            new KeyIndexService();

        _keyIndexService.Build(
            _settings.RedumpKeysPath);


        RedrrxPs3DecPath = _settings.RedrrxPs3DecPath;
        SevenZipPath = _settings.SevenZipPath;
        RedumpKeysPath = _settings.RedumpKeysPath;
        WorkDirectory = _settings.WorkDirectory;
        IsoDestinationPath = _settings.IsoDestinationPath;
        JbDestinationPath = _settings.JbDestinationPath;
        Ps3DecThreadCount = _settings.Ps3DecThreadCount;
        RemovePs3Update = _settings.RemovePs3Update;

        _queuePersistence.Start();

        var staleArtifactsRemoved =
            StaleStagingCleanupService.Cleanup(
                IsoDestinationPath,
                JbDestinationPath);

        if (staleArtifactsRemoved > 0)
        {
            SettingsStatus =
                $"Removed {staleArtifactsRemoved} stale staging artifact(s).";
        }

        RefreshToolStatus();
    }

    [RelayCommand]
    private void SaveSettings()
    {
        var errors = new List<string>();

        if (!File.Exists(RedrrxPs3DecPath))
        {
            errors.Add("PS3Dec (Redrrx) executable not found.");
        }

        if (!File.Exists(SevenZipPath))
        {
            errors.Add("7-Zip executable not found.");
        }

        if (!Directory.Exists(RedumpKeysPath))
        {
            errors.Add("Redump key directory not found.");
        }

        if (Ps3DecThreadCount < 1 || Ps3DecThreadCount > 256)
        {
            errors.Add("PS3Dec thread count must be between 1 and 256.");
        }

        if (string.IsNullOrWhiteSpace(WorkDirectory))
        {
            errors.Add("Work directory cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(IsoDestinationPath))
        {
            errors.Add("ISO destination cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(JbDestinationPath))
        {
            errors.Add("JB destination cannot be empty.");
        }

        if (errors.Count > 0)
        {
            SettingsStatus = "Not saved: " + string.Join(" ", errors);
            return;
        }

        _settings.RedrrxPs3DecPath = RedrrxPs3DecPath;
        _settings.SevenZipPath = SevenZipPath;
        _settings.RedumpKeysPath = RedumpKeysPath;
        _settings.Ps3DecThreadCount = Ps3DecThreadCount;
        _settings.WorkDirectory = WorkDirectory;
        _settings.IsoDestinationPath = IsoDestinationPath;
        _settings.JbDestinationPath = JbDestinationPath;
        _settings.RemovePs3Update = RemovePs3Update;

        _settingsService.Save(_settings);

        SettingsStatus = "Settings saved.";

        RefreshToolStatus();
    }

    [RelayCommand]
    private async Task BrowseRedrrxPs3Dec()
    {
        var path = await PickExecutableAsync("Select ps3decrs.exe");

        if (path is not null)
        {
            RedrrxPs3DecPath = path;
        }
    }

    [RelayCommand]
    private async Task AddLocalGames()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Add PS3 game files",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new FilePickerFileType("PS3 game files and archives")
                {
                    Patterns =
                    [
                        "*.iso",
                        "*.zip",
                        "*.7z",
                        "*.rar"
                    ]
                },
                new FilePickerFileType("All files")
                {
                    Patterns = ["*"]
                }
                ]
            });

        foreach (var file in files)
        {
            Jobs.Add(new GameJob
            {
                SourcePath = file.Path.LocalPath,
                Status = "Waiting",
            });
        }

        if (files.Count > 0)
        {
            await PreflightQueue();
        }
    }

    [RelayCommand]
    private void AddUrl()
    {
        var url = UrlToAdd.Trim();

        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out var uri))
        {
            SettingsStatus =
                "Invalid URL.";
            return;
        }

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            SettingsStatus =
                "Only HTTP and HTTPS URLs are supported.";
            return;
        }

        Jobs.Add(new GameJob
        {
            SourcePath = url,
            SourceType = GameSourceType.Url,
            Status = "Waiting",
            Details = "Waiting to download."
        });

        UrlToAdd = string.Empty;

        SettingsStatus =
            "URL added to queue.";
    }

    [RelayCommand]
    private async Task PreflightQueue()
    {
        var reservations =
            new PreflightReservationSet();

        await PreflightQueueWithReservations(
            reservations);
    }

    private async Task PreflightQueueWithReservations(
        PreflightReservationSet reservations)
    {
        await _preflightCoordinator.PreflightQueueAsync(
            Jobs,
            _keyIndexService,
            RedumpKeysPath,
            SevenZipPath,
            IsoDestinationPath,
            JbDestinationPath,
            reservations);
    }

    [RelayCommand]
    private async Task ProcessReadyJobs()
    {
        if (IsPipelineActive)
        {
            return;
        }

        IsPipelineActive = true;

        try
        {
            _pipelineCancellationTokenSource?.Dispose();

            _pipelineCancellationTokenSource =
                new CancellationTokenSource();


            var pipelineCancellationToken =
                _pipelineCancellationTokenSource.Token;

            /*
             * Preflight everything we can before starting.
             *
             * Local jobs can become Ready immediately. URL jobs that have not
             * downloaded yet remain Waiting until their download completes.
             */
            var preflightReservations =
                new PreflightReservationSet();

            await PreflightQueueWithReservations(
                preflightReservations);

            var queuedJobs =
                Jobs.ToList();

            var processingTasks =
                new List<Task>();

            using var processingGate =
                new SemaphoreSlim(1, 1);

            async Task ProcessQueuedJobAsync(
                GameJob queuedJob)
            {
                await processingGate.WaitAsync();

                try
                {
                    if (pipelineCancellationToken.IsCancellationRequested)
                    {
                        queuedJob.Status = "Error";
                        queuedJob.CurrentStage = "Cancelled";
                        queuedJob.Details = "Processing cancelled.";
                        return;
                    }

                    await ProcessSingleJobAsync(
                        queuedJob,
                        pipelineCancellationToken);
                }
                finally
                {
                    processingGate.Release();
                }
            }

            /*
             * Start any local/already-downloaded Ready jobs immediately.
             *
             * ProcessQueuedJobAsync is protected by processingGate, so there is
             * never more than one extract/decrypt/copy pipeline running at once.
             */
            foreach (var job in queuedJobs)
            {
                if (pipelineCancellationToken.IsCancellationRequested)
                {
                    break;
                }
                if (job.Status == "Ready")
                {
                    processingTasks.Add(
                        ProcessQueuedJobAsync(job));
                }
            }

            var downloadService =
                new DownloadService();

            /*
             * This preflight instance is used for URL jobs as each individual
             * download finishes. That lets processing begin immediately rather
             * than waiting for every URL in the queue to download first.
             */
            var preflightService =
                _preflightCoordinator.CreateService(
                    _keyIndexService,
                    RedumpKeysPath,
                    SevenZipPath,
                    IsoDestinationPath,
                    JbDestinationPath,
                    preflightReservations);

            /*
             * Downloads remain deliberately sequential.
             *
             * As soon as one download completes and passes preflight, its
             * processing task is queued. The next download can then start while
             * that game is extracting/decrypting/copying.
             */
            foreach (var job in queuedJobs)
            {
                if (job.SourceType != GameSourceType.Url ||
                    job.Status == "Complete" ||
                    job.Status == "Already exists")
                {
                    continue;
                }

                /*
                 * A URL job with an existing local source was already handled by
                 * the initial preflight above.
                 */
                if (!string.IsNullOrWhiteSpace(job.LocalSourcePath) &&
                    File.Exists(job.LocalSourcePath))
                {
                    continue;
                }

                /*
                 * Give each download its own cancellation and pause state.
                 * Cancelling one download therefore does not poison every URL
                 * remaining in the queue.
                 */
                _downloadCancellationTokenSource?.Dispose();

                _downloadCancellationTokenSource =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        pipelineCancellationToken);

                _downloadPauseTokenSource =
                    new PauseTokenSource();

                job.Status = "Downloading";
                job.CurrentStage = "Downloading";
                job.Progress = 0;
                job.TransferSpeedMbps = 0;
                job.EtaText = string.Empty;
                job.Details =
                    "Downloading source file...";

                var downloadDirectory =
                    Path.Combine(
                        WorkDirectory,
                        "download",
                        Guid.NewGuid().ToString("N"));

                var downloadProgress =
                    new Progress<TransferProgress>(value =>
                    {
                        job.Progress =
                            value.Percentage;

                        job.TransferSpeedMbps =
                            value.MegabytesPerSecond;

                        if (value.EstimatedRemaining.HasValue)
                        {
                            var eta =
                                value.EstimatedRemaining.Value;

                            job.EtaText =
                                $"Downloading — " +
                                $"{value.MegabytesPerSecond:F1} MB/s — " +
                                $"{eta:hh\\:mm\\:ss} remaining";
                        }
                        else
                        {
                            job.EtaText =
                                $"Downloading — " +
                                $"{value.MegabytesPerSecond:F1} MB/s";
                        }
                    });

                var downloadResult =
                    await downloadService.DownloadAsync(
                        job.SourcePath,
                        downloadDirectory,
                        downloadProgress,
                        _downloadCancellationTokenSource.Token,
                        _downloadPauseTokenSource);

                if (!downloadResult.Success)
                {
                    TemporaryDownloadCleanupService.CleanupDirectory(
                        downloadDirectory,
                        WorkDirectory);

                    job.Status = "Error";
                    job.CurrentStage = "Error";
                    job.Details =
                        downloadResult.Error;
                    continue;
                }

                job.LocalSourcePath =
                    downloadResult.OutputPath;

                job.Status = "Waiting";
                job.CurrentStage = string.Empty;
                job.Progress = 0;
                job.TransferSpeedMbps = 0;
                job.EtaText = string.Empty;
                job.Details =
                    "Download complete. Preflighting...";

                await preflightService.PreflightAsync(
                    job);

                if (job.Status == "Ready")
                {
                    processingTasks.Add(
                        ProcessQueuedJobAsync(job));
                }
                else
                {
                    TemporaryDownloadCleanupService.CleanupFile(
                        job.LocalSourcePath,
                        WorkDirectory);

                    job.LocalSourcePath = null;
                }

            }

            /*
             * All downloads are now finished. Processing may still be working
             * through the queue, so wait for it before ProcessReadyJobs returns.
             */
            await Task.WhenAll(
                processingTasks);

        }
        finally
        {
            _downloadCancellationTokenSource?.Dispose();
            _downloadCancellationTokenSource = null;
            _downloadPauseTokenSource = null;

            _pipelineCancellationTokenSource?.Dispose();
            _pipelineCancellationTokenSource = null;

            IsPipelineActive = false;
        }
    }

    private async Task ProcessSingleJobAsync(
        GameJob job,
        CancellationToken cancellationToken)

    {
        var options = new JobExecutionOptions
        {
            RedrrxPs3DecPath = RedrrxPs3DecPath,
            SevenZipPath = SevenZipPath,
            WorkDirectory = WorkDirectory,
            IsoDestinationPath = IsoDestinationPath,
            JbDestinationPath = JbDestinationPath,
            Ps3DecThreadCount = Ps3DecThreadCount,
            RemovePs3Update = RemovePs3Update
        };

        var jobExecutionService =
            new JobExecutionService(
                _metadataCacheService,
                options);

        await jobExecutionService.ProcessAsync(
            job,
            cancellationToken);

    }


    [RelayCommand]
    private async Task BrowseSevenZip()
    {
        var path = await PickExecutableAsync("Select 7z.exe");

        if (path is not null)
        {
            SevenZipPath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseWorkDirectory()
    {
        var folders = await _window.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select work directory",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            WorkDirectory = folders[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task BrowseIsoDestination()
    {
        var folders =
            await _window.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Select ISO destination",
                    AllowMultiple = false
                });

        if (folders.Count > 0)
        {
            IsoDestinationPath =
                folders[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task BrowseJbDestination()
    {
        var folders =
            await _window.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Select JB destination",
                    AllowMultiple = false
                });

        if (folders.Count > 0)
        {
            JbDestinationPath =
                folders[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task BrowseJobDestination(GameJob? job)
    {
        if (job is null)
        {
            return;
        }

        var folders =
            await _window.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "Select destination for this job",
                    AllowMultiple = false
                });

        if (folders.Count > 0)
        {
            job.DestinationOverride =
                folders[0].Path.LocalPath;
        }
    }

    [RelayCommand]
    private async Task BrowseRedumpKeys()
    {
        var folders = await _window.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select Redump key directory",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            RedumpKeysPath = folders[0].Path.LocalPath;
        }
    }

    private async Task<string?> PickExecutableAsync(string title)
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Executable files")
                    {
                        Patterns = ["*.exe"]
                    }
                ]
            });

        if (files.Count > 0)
        {
            return files[0].Path.LocalPath;
        }

        return null;
    }

    private void RefreshToolStatus()
    {
        Tools.Clear();

        var toolDetectionService = new ToolDetectionService(_settings);

        foreach (var tool in toolDetectionService.DetectTools())
        {
            Tools.Add(tool);
        }
    }

    [RelayCommand]
    private void ScanDestinationLibrary()
    {
        var destinationInspectionService =
            new DestinationInspectionService();

        var games =
            destinationInspectionService.ScanJbGames(
                JbDestinationPath);

        SettingsStatus =
            $"Destination scan found {games.Count:N0} JB game(s) with readable PARAM.SFO metadata.";
    }

    [RelayCommand]
    private async Task RetrySelectedJob()
    {
        if (SelectedJob is null)
        {
            return;
        }

        if (SelectedJob.Status != "Error" &&
            SelectedJob.Status != "Missing key" &&
            SelectedJob.Status != "Invalid key")
        {
            return;
        }

        SelectedJob.Status = "Waiting";
        SelectedJob.Details = string.Empty;
        SelectedJob.CurrentStage = string.Empty;
        SelectedJob.Progress = 0;
        SelectedJob.TransferSpeedMbps = 0;
        SelectedJob.EtaText = string.Empty;

        if (SelectedJob.SourceType == GameSourceType.Url)
        {
            SelectedJob.LocalSourcePath = null;
            SelectedJob.Details = "Waiting to download.";
        }

        await PreflightQueue();
    }

    [RelayCommand]
    private void RemoveSelectedJob()
    {
        if (SelectedJob is null)
        {
            return;
        }

        Jobs.Remove(SelectedJob);
        SelectedJob = null;
    }

    [RelayCommand]
    private void ClearCompletedJobs()
    {
        var completedJobs = Jobs
            .Where(job =>
                job.Status == "Complete" ||
                job.Status == "Already exists")
            .ToList();

        foreach (var job in completedJobs)
        {
            Jobs.Remove(job);
        }
    }
    [RelayCommand]
    private void PauseDownload()
    {
        _downloadPauseTokenSource?.Pause();
    }

    [RelayCommand]
    private void ResumeDownload()
    {
        _downloadPauseTokenSource?.Resume();
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _pipelineCancellationTokenSource?.Cancel();
        _downloadCancellationTokenSource?.Cancel();
    }

    [RelayCommand]
    private void ClearQueue()
    {
        Jobs.Clear();
        SelectedJob = null;
    }

    private static string MakeSafeFolderName(string name)
    {
        name = name
            .Replace("™", string.Empty)
            .Replace("®", string.Empty)
            .Replace("©", string.Empty)
            .Replace(":", string.Empty);

        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidCharacter, '_');
        }

        while (name.Contains("  "))
        {
            name = name.Replace("  ", " ");
        }

        return name.Trim();
    }

}