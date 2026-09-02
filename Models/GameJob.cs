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

    [ObservableProperty]
    public partial string? DetectedIsoName { get; set; }

    [ObservableProperty]
    public partial string? DiscKey { get; set; }

    [ObservableProperty]
    public partial string? OutputPath { get; set; }

    private string? _destinationOverride;

    public string? DestinationOverride
    {
        get => _destinationOverride;
        set => SetProperty(ref _destinationOverride, value);
    }

    [ObservableProperty]
    public partial string? TitleId { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string CurrentStage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double TransferSpeedMbps { get; set; }

    [ObservableProperty]
    public partial string EtaText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial OutputFormat OutputFormat { get; set; } =
    OutputFormat.DecryptedIso;

    public static OutputFormatOption[] AvailableOutputFormats { get; } =
    [
        new()
    {
        Value = OutputFormat.DecryptedIso,
        Name = "Decrypted ISO"
    },
    new()
    {
        Value = OutputFormat.JbFolder,
        Name = "JB Folder"
    }
    ];
}