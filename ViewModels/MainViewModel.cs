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

namespace PS3Butler.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly Window _window;
    private readonly KeyIndexService _keyIndexService;
    private readonly MetadataCacheService _metadataCacheService;

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
                Status = "Waiting"
            });
        }

        if (files.Count > 0)
        {
            await PreflightQueue();
        }
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
            await preflightService.PreflightAsync(job);
        }
    }

    [RelayCommand]
    private async Task ProcessReadyJobs()
    {
        var ps3DecService =
            new Ps3DecService(
                RedrrxPs3DecPath,
                Ps3DecThreadCount);

        var archiveExtractionService =
            new ArchiveExtractionService(
                SevenZipPath);

        var deliveryService =
            new DeliveryService();

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

            string inputIsoPath;
            string? temporaryExtractionDirectory = null;

            var extension = Path.GetExtension(job.SourcePath);

            if (extension.Equals(
                    ".iso",
                    StringComparison.OrdinalIgnoreCase))
            {
                inputIsoPath = job.SourcePath;
            }
            else
            {
                job.Status = "Extracting";
                job.CurrentStage = "Extracting";
                job.Progress = 0;
                job.Details = "Extracting ISO from archive...";

                temporaryExtractionDirectory =
                    Path.Combine(
                        WorkDirectory,
                        Path.GetFileNameWithoutExtension(job.SourcePath));

                var extractionProgress =
                    new Progress<double>(value =>
                    {
                        job.Progress = value;
                    });

                var extractionResult =
                    await archiveExtractionService.ExtractIsoAsync(
                        job.SourcePath,
                        temporaryExtractionDirectory,
                        extractionProgress);

                if (!extractionResult.Success)
                {
                    job.Status = "Error";
                    job.Details = extractionResult.Error;
                    continue;
                }

                inputIsoPath = extractionResult.IsoPath;
            }

            job.Status = "Decrypting";
            job.CurrentStage = "Decrypting";
            job.Progress = 0;
            job.Details = "Decrypting ISO with ps3decrs...";

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
                job.Details = decryptResult.Error;
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
                        job.DetectedIsoName ?? job.SourcePath);

                _metadataCacheService.AddOrUpdate(
                    gameName,
                    metadata.TitleId,
                    metadata.Title);

                job.TitleId = metadata.TitleId;
            }
            job.Status = "Copying";
            job.CurrentStage = "Copying";
            job.Progress = 0;
            job.Details = "Copying decrypted ISO to destination...";

            var copyProgress =
                new Progress<double>(value =>
                {
                    job.Progress = value;
                });

            var deliveryResult =
                await deliveryService.CopyAsync(
                    decryptResult.OutputPath,
                    DefaultDestinationPath,
                    copyProgress);

            if (!deliveryResult.Success)
            {
                job.Status = "Error";
                job.Details = deliveryResult.Error;
                continue;
            }

            job.OutputPath = deliveryResult.DestinationPath;

            try
            {
                if (File.Exists(decryptResult.OutputPath))
                {
                    File.Delete(decryptResult.OutputPath);
                }

                if (temporaryExtractionDirectory is not null &&
                    Directory.Exists(temporaryExtractionDirectory))
                {
                    Directory.Delete(
                        temporaryExtractionDirectory,
                        recursive: true);
                }
            }
            catch (Exception ex)
            {
                job.Status = "Complete";
                job.Details =
                    $"Copied successfully, but temporary cleanup failed: {ex.Message}";
                continue;
            }

            job.Status = "Complete";
            job.Progress = 100;
            job.Details =
                $"Decrypted ISO copied to: {deliveryResult.DestinationPath}";
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
    private void ClearQueue()
    {
        Jobs.Clear();
        SelectedJob = null;
    }

}