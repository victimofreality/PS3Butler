namespace PS3Butler.Services.Execution;

public sealed class ProcessRunResult
{
    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public bool WasCancelled { get; init; }

    public bool TimedOut { get; init; }

    public bool Success =>
        !WasCancelled &&
        !TimedOut &&
        ExitCode == 0;
}