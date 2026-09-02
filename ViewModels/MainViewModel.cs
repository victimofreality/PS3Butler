using PS3Butler.Configuration;
using PS3Butler.Models;
using PS3Butler.Services;
using System.Collections.ObjectModel;

namespace PS3Butler.ViewModels;

public class MainViewModel : ViewModelBase
{
    public string Greeting { get; } = "PS3 Butler";

    public ObservableCollection<ToolStatus> Tools { get; } = [];

    public MainViewModel()
    {
        var settingsService = new SettingsService();
        var settings = settingsService.Load();

        var toolDetectionService = new ToolDetectionService(settings);

        foreach (var tool in toolDetectionService.DetectTools())
        {
            Tools.Add(tool);
        }
    }
}