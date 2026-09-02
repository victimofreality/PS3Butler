using CommunityToolkit.Mvvm.ComponentModel;
using PS3Butler.Models;
using PS3Butler.Services;
using System.Collections.ObjectModel;

namespace PS3Butler.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public string Greeting { get; } = "PS3 Butler";

    public ObservableCollection<ToolStatus> Tools { get; } = [];

    public MainViewModel()
    {
        var toolDetectionService = new ToolDetectionService();

        foreach (var tool in toolDetectionService.DetectTools())
        {
            Tools.Add(tool);
        }
    }
}