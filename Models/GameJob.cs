using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;

namespace PS3Butler.Models;

public enum GameSourceType
{
    LocalFile,
    Url
}

public partial class GameJob : ObservableObject
{
    public string SourcePath { get; set; } = string.Empty;
    public GameSourceType SourceType { get; set; } =
      GameSourceType.LocalFile;

    public string? LocalSourcePath { get; set; }

    public string EffectiveSourcePath =>
        SourceType == GameSourceType.Url &&
        !string.IsNullOrWhiteSpace(LocalSourcePath)
            ? LocalSourcePath
            : SourcePath;

    public string DisplayName
    {
        get
        {
            if (SourceType == GameSourceType.Url)
            {
                if (Uri.TryCreate(
                        SourcePath,
                        UriKind.Absolute,
                        out var uri))
                {
                    var fileName =
                        Path.GetFileName(
                            Uri.UnescapeDataString(
                                uri.AbsolutePath));

                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        return fileName;
                    }
                }

                return SourcePath;
            }

            return Path.GetFileName(SourcePath);
        }
    }

    private bool _isSynchronizingState;

    [ObservableProperty]
    public partial JobState State { get; set; } =
        JobState.Waiting;

    [ObservableProperty]
    public partial JobStage Stage { get; set; } =
        JobStage.Waiting;

    [ObservableProperty]
    public partial string Status { get; set; } =
        "Waiting";

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

    partial void OnStatusChanged(
        string value)
    {
        if (_isSynchronizingState)
        {
            return;
        }

        var mappedState =
            MapStatusToState(
                value);

        if (!mappedState.HasValue ||
            mappedState.Value == State)
        {
            return;
        }

        _isSynchronizingState = true;

        try
        {
            State =
                mappedState.Value;
        }
        finally
        {
            _isSynchronizingState = false;
        }
    }

    partial void OnCurrentStageChanged(
        string value)
    {
        if (_isSynchronizingState)
        {
            return;
        }

        var mappedStage =
            MapCurrentStageToStage(
                value);

        if (mappedStage == Stage)
        {
            return;
        }

        _isSynchronizingState = true;

        try
        {
            Stage =
                mappedStage;
        }
        finally
        {
            _isSynchronizingState = false;
        }
    }

    partial void OnStateChanged(
        JobState value)
    {
        if (_isSynchronizingState)
        {
            return;
        }

        _isSynchronizingState = true;

        try
        {
            Status =
                GetStatusText(
                    value,
                    Stage);
        }
        finally
        {
            _isSynchronizingState = false;
        }
    }

    partial void OnStageChanged(
        JobStage value)
    {
        if (_isSynchronizingState)
        {
            return;
        }

        _isSynchronizingState = true;

        try
        {
            CurrentStage =
                GetStageText(
                    value);

            if (State == JobState.Running)
            {
                Status =
                    GetStatusText(
                        State,
                        value);
            }
        }
        finally
        {
            _isSynchronizingState = false;
        }
    }

    private static JobState? MapStatusToState(
        string status)
    {
        return status switch
        {
            "Waiting" =>
                JobState.Waiting,

            "Ready" =>
                JobState.Ready,

            "Downloading" or
            "Inspecting archive" or
            "Extracting" or
            "Decrypting" or
            "Reading metadata" or
            "Creating JB folder" or
            "Copying" or
            "Cleaning up" =>
                JobState.Running,

            "Complete" =>
                JobState.Complete,

            "Already exists" =>
                JobState.AlreadyExists,

            "Missing key" =>
                JobState.MissingKey,

            "Invalid key" =>
                JobState.InvalidKey,

            "Error" =>
                JobState.Error,

            _ =>
                null
        };
    }

    private static JobStage MapCurrentStageToStage(
        string currentStage)
    {
        if (string.IsNullOrWhiteSpace(
                currentStage))
        {
            return JobStage.None;
        }

        if (currentStage.Equals(
                "Waiting",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.Waiting;
        }

        if (currentStage.StartsWith(
                "Downloading",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.Downloading;
        }

        if (currentStage.StartsWith(
                "Preflight",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.Preflight;
        }

        if (currentStage.StartsWith(
                "Inspecting",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.InspectingArchive;
        }

        if (currentStage.StartsWith(
                "Extracting",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.ExtractingArchive;
        }

        if (currentStage.StartsWith(
                "Decrypting",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.Decrypting;
        }

        if (currentStage.StartsWith(
                "Reading",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.ReadingMetadata;
        }

        if (currentStage.StartsWith(
                "Creating",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.CreatingJbFolder;
        }

        if (currentStage.StartsWith(
                "Copying",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.Copying;
        }

        if (currentStage.StartsWith(
                "Cleaning",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.CleaningUp;
        }

        if (currentStage.Equals(
                "Complete",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.Complete;
        }

        if (currentStage.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.Cancelled;
        }

        if (currentStage.Equals(
                "Error",
                StringComparison.OrdinalIgnoreCase))
        {
            return JobStage.Error;
        }

        return JobStage.None;
    }

    private static string GetStatusText(
        JobState state,
        JobStage stage)
    {
        return state switch
        {
            JobState.Waiting =>
                "Waiting",

            JobState.Ready =>
                "Ready",

            JobState.Running =>
                stage switch
                {
                    JobStage.Downloading =>
                        "Downloading",

                    JobStage.InspectingArchive =>
                        "Inspecting archive",

                    JobStage.ExtractingArchive =>
                        "Extracting",

                    JobStage.Decrypting =>
                        "Decrypting",

                    JobStage.ReadingMetadata =>
                        "Reading metadata",

                    JobStage.CreatingJbFolder =>
                        "Creating JB folder",

                    JobStage.Copying =>
                        "Copying",

                    JobStage.CleaningUp =>
                        "Cleaning up",

                    _ =>
                        "Running"
                },

            JobState.Complete =>
                "Complete",

            JobState.AlreadyExists =>
                "Already exists",

            JobState.MissingKey =>
                "Missing key",

            JobState.InvalidKey =>
                "Invalid key",

            JobState.Error =>
                "Error",

            _ =>
                state.ToString()
        };
    }

    private static string GetStageText(
        JobStage stage)
    {
        return stage switch
        {
            JobStage.None =>
                string.Empty,

            JobStage.Waiting =>
                "Waiting",

            JobStage.Downloading =>
                "Downloading",

            JobStage.Preflight =>
                "Preflight",

            JobStage.InspectingArchive =>
                "Inspecting archive",

            JobStage.ExtractingArchive =>
                "Extracting archive",

            JobStage.Decrypting =>
                "Decrypting",

            JobStage.ReadingMetadata =>
                "Reading metadata",

            JobStage.CreatingJbFolder =>
                "Creating JB folder",

            JobStage.Copying =>
                "Copying",

            JobStage.CleaningUp =>
                "Cleaning up",

            JobStage.Complete =>
                "Complete",

            JobStage.Error =>
                "Error",

            _ =>
                stage.ToString()
        };
    }

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
    },
    new()
    {
        Value = OutputFormat.EncryptedIsoWithKey,
        Name = "Encrypted ISO + key"
    }
    ];
}
