using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PS3Butler.Configuration;
using PS3Butler.Models;
using PS3Butler.Services;
using System.Collections.ObjectModel;

namespace PS3Butler.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;

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

    public MainViewModel()
    {
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