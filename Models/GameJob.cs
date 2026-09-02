using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace PS3Butler.Models;

public partial class GameJob : ObservableObject
{
    public string SourcePath { get; set; } = string.Empty;

    public string DisplayName =>
        Path.GetFileName(SourcePath);

    [ObservableProperty]
    public partial string Status { get; set; } = "Waiting";

    [ObservableProperty]
    public partial string Details { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? KeyPath { get; set; }
}