using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PS3Butler.Configuration;
using PS3Butler.Models;
using PS3Butler.Services;
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
    public partial string ClassicPs3DecPath { get; set; }

    [ObservableProperty]
    public partial string SevenZipPath { get; set; }

    [ObservableProperty]
    public partial string RedumpKeysPath { get; set; }

    [ObservableProperty]
    public partial int Ps3DecThreadCount { get; set; }

    [ObservableProperty]
    public partial string WorkDirectory { get; set; }

    [ObservableProperty]
    public partial string DefaultDestinationPath { get; set; }

    [ObservableProperty]
    public partial string SettingsStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool RemovePs3Update { get; set; }

    [ObservableProperty]
    public partial string UrlToAdd { get; set; } = string.Empty;

    public string SettingsLocation => _settingsService.SettingsFilePath;

    public string SettingsMode =>
        _settingsService.IsPortableMode ? "Portable" : "Normal";

    public MainViewModel(Window window)
    {
        _window = window;

        _settingsService = new SettingsService();
        _metadataCacheService =
            new MetadataCacheService(_settingsService);
        _settings = _settingsService.Load();
        _keyIndexService = new KeyIndexService();
        _keyIndexService.Build(_settings.RedumpKeysPath);


        RedrrxPs3DecPath = _settings.RedrrxPs3DecPath;
        ClassicPs3DecPath = _settings.ClassicPs3DecPath;
        SevenZipPath = _settings.SevenZipPath;
        RedumpKeysPath = _settings.RedumpKeysPath;
        WorkDirectory = _settings.WorkDirectory;
        DefaultDestinationPath = _settings.DefaultDestinationPath;
        Ps3DecThreadCount = _settings.Ps3DecThreadCount;
        RemovePs3Update = _settings.RemovePs3Update;

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

        if (!File.Exists(ClassicPs3DecPath))
        {
            errors.Add("PS3Dec (Classic) executable not found.");
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

        if (string.IsNullOrWhiteSpace(DefaultDestinationPath))
        {
            errors.Add("Default destination cannot be empty.");
        }

        if (errors.Count > 0)
        {
            SettingsStatus = "Not saved: " + string.Join(" ", errors);
            return;
        }

        _settings.RedrrxPs3DecPath = RedrrxPs3DecPath;
        _settings.ClassicPs3DecPath = ClassicPs3DecPath;
        _settings.SevenZipPath = SevenZipPath;
        _settings.RedumpKeysPath = RedumpKeysPath;
        _settings.Ps3DecThreadCount = Ps3DecThreadCount;
        _settings.WorkDirectory = WorkDirectory;
        _settings.DefaultDestinationPath = DefaultDestinationPath;
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
        _keyIndexService.Build(RedumpKeysPath);

        var archiveInspectionService =
            new ArchiveInspectionService(SevenZipPath);

        var destinationInspectionService =
            new DestinationInspectionService();

        var preflightService =
            new GamePreflightService(
                _keyIndexService,
                archiveInspectionService,
                destinationInspectionService,
                _metadataCacheService,
                DefaultDestinationPath);

        foreach (var job in Jobs)
        {
            if (job.Status == "Complete")
            {
                continue;
            }

            await preflightService.PreflightAsync(job);
        }
    }

    [RelayCommand]
    private async Task ProcessReadyJobs()
    {
        var downloadService =
            new DownloadService();

        _downloadCancellationTokenSource =
            new CancellationTokenSource();

        _downloadPauseTokenSource =
            new PauseTokenSource();

        foreach (var job in Jobs)
        {
            if (job.SourceType != GameSourceType.Url ||
                job.Status == "Complete" ||
                job.Status == "Already exists")
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(job.LocalSourcePath) &&
                File.Exists(job.LocalSourcePath))
            {
                continue;
            }

            job.Status = "Downloading";
            job.CurrentStage = "Downloading";
            job.Progress = 0;
            job.TransferSpeedMbps = 0;
            job.EtaText = string.Empty;
            job.Details = "Downloading source file...";

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
                "Download complete. Waiting for preflight.";
        }

        await PreflightQueue();

        var ps3DecService = new Ps3DecService(
                RedrrxPs3DecPath,
                Ps3DecThreadCount);

        var archiveExtractionService =
            new ArchiveExtractionService(
                SevenZipPath);

        var jbExtractionService =
            new JbExtractionService(
                SevenZipPath);

        var deliveryService =
            new DeliveryService();

        var folderDeliveryService =
            new FolderDeliveryService();

        var decryptedIsoMetadataService =
            new DecryptedIsoMetadataService(
                SevenZipPath);

        foreach (var job in Jobs)
        {
            if (job.Status != "Ready")
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(job.DiscKey))
            {
                job.Status = "Error";
                job.Details = "Disc key is missing.";
                continue;
            }

            var destinationPath =
                string.IsNullOrWhiteSpace(job.DestinationOverride)
                    ? DefaultDestinationPath
                    : job.DestinationOverride;

            job.Progress = 0;
            job.CurrentStage = string.Empty;
            job.EtaText = string.Empty;
            job.TransferSpeedMbps = 0;

            string inputIsoPath;
            string? temporaryExtractionDirectory = null;
            string? temporaryJbDirectory = null;

            var sourcePath =
                job.EffectiveSourcePath;

            var extension =
                Path.GetExtension(sourcePath);

            if (extension.Equals(
                    ".iso",
                    StringComparison.OrdinalIgnoreCase))
            {
                inputIsoPath =
                    sourcePath;
            }
            else
            {
                job.Status = "Extracting";
                job.CurrentStage = "Extracting archive";
                job.Progress = 0;
                job.Details =
                    "Extracting ISO from archive...";

                temporaryExtractionDirectory =
                    Path.Combine(
                        WorkDirectory,
                        "archive",
                        Guid.NewGuid().ToString("N"));

                var extractionProgress =
                    new Progress<double>(value =>
                    {
                        job.Progress = value;
                    });

                var extractionResult =
                    await archiveExtractionService.ExtractIsoAsync(
                        sourcePath,
                        temporaryExtractionDirectory,
                        extractionProgress);

                if (!extractionResult.Success)
                {
                    job.Status = "Error";
                    job.CurrentStage = "Error";
                    job.Details =
                        extractionResult.Error;
                    continue;
                }

                inputIsoPath =
                    extractionResult.IsoPath;
            }
            if (job.OutputFormat ==
                OutputFormat.EncryptedIsoWithKey)
            {
                job.Status = "Copying";
                job.CurrentStage = "Copying encrypted ISO";
                job.Progress = 0;
                job.TransferSpeedMbps = 0;
                job.EtaText = string.Empty;
                job.Details =
                    "Copying encrypted ISO and disc key to destination...";

                var finalIsoFileName =
                    job.DetectedIsoName ??
                    Path.GetFileName(inputIsoPath);

                var copyProgress =
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
                                $"Copying — " +
                                $"{value.MegabytesPerSecond:F1} MB/s — " +
                                $"{eta:hh\\:mm\\:ss} remaining";
                        }
                        else
                        {
                            job.EtaText =
                                $"Copying — " +
                                $"{value.MegabytesPerSecond:F1} MB/s";
                        }
                    });

                var deliveryResult =
                    await deliveryService.CopyAsync(
                        inputIsoPath,
                        destinationPath,
                        copyProgress,
                        finalIsoFileName);

                if (!deliveryResult.Success)
                {
                    job.Status = "Error";
                    job.CurrentStage = "Error";
                    job.Details =
                        deliveryResult.Error;
                    continue;
                }

                var destinationKeyPath =
                    Path.ChangeExtension(
                        deliveryResult.DestinationPath,
                        ".dkey");

                await File.WriteAllTextAsync(
                    destinationKeyPath,
                    job.DiscKey);

                job.OutputPath =
                    deliveryResult.DestinationPath;

                try
                {
                    if (temporaryExtractionDirectory is not null &&
                        Directory.Exists(
                            temporaryExtractionDirectory))
                    {
                        Directory.Delete(
                            temporaryExtractionDirectory,
                            recursive: true);
                    }

                    if (job.SourceType == GameSourceType.Url &&
                        !string.IsNullOrWhiteSpace(job.LocalSourcePath) &&
                        File.Exists(job.LocalSourcePath))
                    {
                        var downloadDirectory =
                            Path.GetDirectoryName(job.LocalSourcePath);

                        File.Delete(job.LocalSourcePath);

                        if (!string.IsNullOrWhiteSpace(downloadDirectory) &&
                            Directory.Exists(downloadDirectory) &&
                            !Directory.EnumerateFileSystemEntries(downloadDirectory).Any())
                        {
                            Directory.Delete(downloadDirectory);
                        }

                        job.LocalSourcePath = null;
                    }
                }
                catch (Exception ex)
                {
                    job.Status = "Complete";
                    job.CurrentStage = "Complete";
                    job.Progress = 100;
                    job.EtaText = string.Empty;
                    job.Details =
                        $"Encrypted ISO and key copied successfully, but temporary cleanup failed: {ex.Message}";
                    continue;
                }
                job.Status = "Complete";
                job.CurrentStage = "Complete";
                job.Progress = 100;
                job.EtaText = string.Empty;
                job.Details =
                    $"Encrypted ISO and key copied to: {job.OutputPath}";

                continue;
            }

            job.Status = "Decrypting";
            job.CurrentStage = "Decrypting";
            job.Progress = 0;
            job.Details =
                "Decrypting ISO with ps3decrs...";

            var decryptProgress =
                new Progress<double>(value =>
                {
                    job.Progress = value;
                });

            var decryptResult =
                await ps3DecService.DecryptAsync(
                    inputIsoPath,
                    job.DiscKey,
                    WorkDirectory,
                    decryptProgress);

            if (!decryptResult.Success)
            {
                job.Status = "Error";
                job.CurrentStage = "Error";
                job.Details =
                    decryptResult.Error;
                continue;
            }

            var metadata =
                await decryptedIsoMetadataService.ReadMetadataAsync(
                    decryptResult.OutputPath,
                    WorkDirectory);

            if (metadata is not null &&
                !string.IsNullOrWhiteSpace(metadata.TitleId))
            {
                var gameName =
                    Path.GetFileNameWithoutExtension(
                        job.DetectedIsoName ??
                        sourcePath);

                _metadataCacheService.AddOrUpdate(
                    gameName,
                    metadata.TitleId,
                    metadata.Title);

                job.TitleId =
                    metadata.TitleId;
            }

            if (job.OutputFormat ==
                OutputFormat.DecryptedIso)
            {
                job.Status = "Copying";
                job.CurrentStage = "Copying ISO";
                job.Progress = 0;
                job.TransferSpeedMbps = 0;
                job.EtaText = string.Empty;
                job.Details =
                    "Copying decrypted ISO to destination...";

                var copyProgress =
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
                                $"Copying — " +
                                $"{value.MegabytesPerSecond:F1} MB/s — " +
                                $"{eta:hh\\:mm\\:ss} remaining";
                        }
                        else
                        {
                            job.EtaText =
                                $"Copying — " +
                                $"{value.MegabytesPerSecond:F1} MB/s";
                        }
                    });

                var finalIsoFileName =
                    job.DetectedIsoName ??
                    Path.GetFileName(sourcePath);

                var deliveryResult =
                    await deliveryService.CopyAsync(
                        decryptResult.OutputPath,
                        destinationPath,
                        copyProgress,
                        finalIsoFileName);

                if (!deliveryResult.Success)
                {
                    job.Status = "Error";
                    job.CurrentStage = "Error";
                    job.Details =
                        deliveryResult.Error;
                    continue;
                }

                job.OutputPath =
                    deliveryResult.DestinationPath;
            }
            else if (job.OutputFormat ==
                     OutputFormat.JbFolder)
            {
                job.Status = "Extracting";
                job.CurrentStage = "Creating JB folder";
                job.Progress = 0;
                job.Details =
                    "Extracting decrypted ISO to JB folder...";

                string gameFolderName;

                if (metadata is not null &&
                    !string.IsNullOrWhiteSpace(metadata.TitleId) &&
                    !string.IsNullOrWhiteSpace(metadata.Title))
                {
                    gameFolderName =
                        MakeSafeFolderName(
                            $"{metadata.Title} [{metadata.TitleId}]");
                }
                else
                {
                    gameFolderName =
                        MakeSafeFolderName(
                            Path.GetFileNameWithoutExtension(
                                job.DetectedIsoName ??
                                sourcePath));
                }
                temporaryJbDirectory =
                    Path.Combine(
                        WorkDirectory,
                        "jb",
                        Guid.NewGuid().ToString("N"));

                var jbProgress =
                    new Progress<double>(value =>
                    {
                        job.Progress = value;
                    });

                var jbExtractionResult =
                    await jbExtractionService.ExtractAsync(
                        decryptResult.OutputPath,
                        temporaryJbDirectory,
                        jbProgress);

                if (!jbExtractionResult.Success)
                {
                    job.Status = "Error";
                    job.CurrentStage = "Error";
                    job.Details =
                        jbExtractionResult.Error;
                    continue;
                }

                if (RemovePs3Update)
                {
                    var ps3UpdateDirectory =
                        Path.Combine(
                            temporaryJbDirectory,
                            "PS3_UPDATE");

                    if (Directory.Exists(ps3UpdateDirectory))
                    {
                        Directory.Delete(
                            ps3UpdateDirectory,
                            recursive: true);
                    }
                }
                var finalJbDestination =
                    Path.Combine(
                        destinationPath,
                        gameFolderName);

                job.Status = "Copying";
                job.CurrentStage = "Copying JB folder";
                job.Progress = 0;
                job.TransferSpeedMbps = 0;
                job.EtaText = string.Empty;
                job.Details =
                    "Copying JB folder to destination...";

                var folderCopyProgress =
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
                                $"Copying — " +
                                $"{value.MegabytesPerSecond:F1} MB/s — " +
                                $"{eta:hh\\:mm\\:ss} remaining";
                        }
                        else
                        {
                            job.EtaText =
                                $"Copying — " +
                                $"{value.MegabytesPerSecond:F1} MB/s";
                        }
                    });

                var folderDeliveryResult =
                    await folderDeliveryService.CopyAsync(
                        temporaryJbDirectory,
                        finalJbDestination,
                        folderCopyProgress);

                if (!folderDeliveryResult.Success)
                {
                    job.Status = "Error";
                    job.CurrentStage = "Error";
                    job.Details =
                        folderDeliveryResult.Error;
                    continue;
                }

                job.OutputPath =
                    folderDeliveryResult.DestinationPath;
            }

            try
            {
                if (File.Exists(
                        decryptResult.OutputPath))
                {
                    File.Delete(
                        decryptResult.OutputPath);
                }

                if (temporaryExtractionDirectory is not null &&
                    Directory.Exists(
                        temporaryExtractionDirectory))
                {
                    Directory.Delete(
                        temporaryExtractionDirectory,
                        recursive: true);
                }

                if (temporaryJbDirectory is not null &&
                    Directory.Exists(
                        temporaryJbDirectory))
                {
                    Directory.Delete(
                        temporaryJbDirectory,
                        recursive: true);
                }
                if (job.SourceType == GameSourceType.Url &&
                    !string.IsNullOrWhiteSpace(job.LocalSourcePath) &&
                    File.Exists(job.LocalSourcePath))
                {
                    var downloadDirectory =
                        Path.GetDirectoryName(job.LocalSourcePath);

                    File.Delete(job.LocalSourcePath);

                    if (!string.IsNullOrWhiteSpace(downloadDirectory) &&
                        Directory.Exists(downloadDirectory) &&
                        !Directory.EnumerateFileSystemEntries(downloadDirectory).Any())
                    {
                        Directory.Delete(downloadDirectory);
                    }

                    job.LocalSourcePath = null;
                }
            }
            catch (Exception ex)
            {
                job.Status = "Complete";
                job.CurrentStage = "Complete";
                job.Progress = 100;
                job.EtaText = string.Empty;
                job.Details =
                    $"Output completed successfully, but temporary cleanup failed: {ex.Message}";
                continue;
            }

            job.Status = "Complete";
            job.CurrentStage = "Complete";
            job.Progress = 100;
            job.EtaText = string.Empty;

            if (job.OutputFormat ==
                OutputFormat.DecryptedIso)
            {
                job.Details =
                    $"Decrypted ISO copied to: {job.OutputPath}";
            }
            else
            {
                job.Details =
                    $"JB folder copied to: {job.OutputPath}";
            }
        }
    }

    [RelayCommand]
    private async Task BrowseClassicPs3Dec()
    {
        var path = await PickExecutableAsync("Select PS3Dec.exe");

        if (path is not null)
        {
            ClassicPs3DecPath = path;
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
    private async Task BrowseDefaultDestination()
    {
        var folders = await _window.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select default destination",
                AllowMultiple = false
            });

        if (folders.Count > 0)
        {
            DefaultDestinationPath = folders[0].Path.LocalPath;
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
                DefaultDestinationPath);

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