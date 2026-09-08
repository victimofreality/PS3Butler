using System;
using System.Collections.Generic;

namespace PS3Butler.Services.Execution;

public sealed class ProcessRunRequest
{
    public required string FileName { get; init; }

    public string Arguments { get; init; } = string.Empty;

    public IReadOnlyList<string>? ArgumentList { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool RedirectStandardOutput { get; init; } = true;

    public bool RedirectStandardError { get; init; } = true;

    public TimeSpan? Timeout { get; init; }

    public Action<string>? StandardOutputChunkReceived { get; init; }

    public Action<string>? StandardErrorChunkReceived { get; init; }
}