namespace PS3Butler.Services.Pipeline;

public sealed class PipelineRunSettings
{
    public required string RedrrxPs3DecPath { get; init; }

    public required string SevenZipPath { get; init; }

    public required string RedumpKeysPath { get; init; }

    public required string WorkDirectory { get; init; }

    public required string IsoDestinationPath { get; init; }

    public required string JbDestinationPath { get; init; }

    public required int Ps3DecThreadCount { get; init; }

    public required bool RemovePs3Update { get; init; }
}
