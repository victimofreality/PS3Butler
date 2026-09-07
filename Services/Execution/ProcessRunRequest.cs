using System;

namespace PS3Butler.Services.Execution;

public sealed class ProcessRunRequest
{
    public required string FileName { get; init; }

    public string Arguments { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }

    public bool RedirectStandardOutput { get; init; } = true;

    public bool RedirectStandardError { get; init; } = true;

    public TimeSpan? Timeout { get; init; }
}