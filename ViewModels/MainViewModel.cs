using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PS3Butler.Configuration;
using PS3Butler.Models;
using PS3Butler.Services;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace PS3Butler.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly Window _window;

    public string Greeting { get; } = "PS3 Butler";

    public ObservableCollection<ToolStatus> Tools { get; } = [];

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
    public partial string SettingsStatus { get; set; } = string.Empty;

    public string SettingsLocation => _settingsService.SettingsFilePath;

    public string SettingsMode =>
        _settingsService.IsPortableMode ? "Portable" : "Normal";

    public MainViewModel(Window window)
    {
        _window = window;

        _settingsService = new SettingsService();
        _settings = _settingsService.Load();

        RedrrxPs3DecPath = _settings.RedrrxPs3DecPath;
        ClassicPs3DecPath = _settings.ClassicPs3DecPath;
        SevenZipPath = _settings.SevenZipPath;
        RedumpKeysPath = _settings.RedumpKeysPath;
        Ps3DecThreadCount = _settings.Ps3DecThreadCount;

        RefreshToolStatus();
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settings.RedrrxPs3DecPath = RedrrxPs3DecPath;
        _settings.ClassicPs3DecPath = ClassicPs3DecPath;
        _settings.SevenZipPath = SevenZipPath;
        _settings.RedumpKeysPath = RedumpKeysPath;
        _settings.Ps3DecThreadCount = Ps3DecThreadCount;

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
}