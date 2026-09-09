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
    private readonly PipelineRunCoordinator _pipelineRunCoordinator;
    private int _toolStatusRefreshVersion;

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

        _pipelineRunCoordinator =
            new PipelineRunCoordinator(
                _keyIndexService,
                _metadataCacheService,
                _preflightCoordinator);


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

        _ = RefreshToolStatusAsync();
    }

    [RelayCommand]
    private async Task SaveSettings()
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

        await RefreshToolStatusAsync();
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
                State = JobState.Waiting,
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
            State = JobState.Waiting,
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
            var runSettings =
                new PipelineRunSettings
                {
                    RedrrxPs3DecPath =
                        RedrrxPs3DecPath,

                    SevenZipPath =
                        SevenZipPath,

                    RedumpKeysPath =
                        RedumpKeysPath,

                    WorkDirectory =
                        WorkDirectory,

                    IsoDestinationPath =
                        IsoDestinationPath,

                    JbDestinationPath =
                        JbDestinationPath,

                    Ps3DecThreadCount =
                        Ps3DecThreadCount,

                    RemovePs3Update =
                        RemovePs3Update
                };

            await _pipelineRunCoordinator.RunAsync(
                Jobs,
                runSettings);
        }
        finally
        {
            IsPipelineActive = false;
        }
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

    private async Task RefreshToolStatusAsync()
    {
        var refreshVersion = ++_toolStatusRefreshVersion;
        var detectionSettings = new AppSettings
        {
            RedrrxPs3DecPath = _settings.RedrrxPs3DecPath,
            SevenZipPath = _settings.SevenZipPath,
            RedumpKeysPath = _settings.RedumpKeysPath
        };

        try
        {
            var detectedTools = await Task.Run(() =>
                new ToolDetectionService(detectionSettings).DetectTools());

            // A settings save may have started a newer scan while this one ran.
            if (refreshVersion != _toolStatusRefreshVersion)
            {
                return;
            }

            Tools.Clear();

            foreach (var tool in detectedTools)
            {
                Tools.Add(tool);
            }
        }
        catch (Exception ex)
        {
            if (refreshVersion == _toolStatusRefreshVersion)
            {
                SettingsStatus = $"Could not check tools: {ex.Message}";
            }
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

        if (SelectedJob.State != JobState.Error &&
            SelectedJob.State != JobState.MissingKey &&
            SelectedJob.State != JobState.InvalidKey)
        {
            return;
        }

        SelectedJob.State = JobState.Waiting;
        SelectedJob.Details = string.Empty;
        SelectedJob.Stage = JobStage.None;
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
                job.State == JobState.Complete ||
                job.State == JobState.AlreadyExists)
            .ToList();

        foreach (var job in completedJobs)
        {
            Jobs.Remove(job);
        }
    }
    [RelayCommand]
    private void PauseDownload()
    {
        _pipelineRunCoordinator.PauseDownload();
    }

    [RelayCommand]
    private void ResumeDownload()
    {
        _pipelineRunCoordinator.ResumeDownload();
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _pipelineRunCoordinator.Cancel();
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
