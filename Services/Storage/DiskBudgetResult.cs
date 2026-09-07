namespace PS3Butler.Services.Storage;

public sealed class DiskBudgetResult
{
    public bool IsSufficient { get; init; }

    public long RequiredBytes { get; init; }

    public long AvailableBytes { get; init; }

    public long SafetyMarginBytes { get; init; }

    public string Message { get; init; } = string.Empty;

    public static DiskBudgetResult Ok(
        long requiredBytes = 0,
        long availableBytes = long.MaxValue,
        long safetyMarginBytes = 0)
    {
        return new DiskBudgetResult
        {
            IsSufficient = true,
            RequiredBytes = requiredBytes,
            AvailableBytes = availableBytes,
            SafetyMarginBytes = safetyMarginBytes
        };
    }

    public static DiskBudgetResult Insufficient(
        long requiredBytes,
        long availableBytes,
        long safetyMarginBytes,
        string message)
    {
        return new DiskBudgetResult
        {
            IsSufficient = false,
            RequiredBytes = requiredBytes,
            AvailableBytes = availableBytes,
            SafetyMarginBytes = safetyMarginBytes,
            Message = message
        };
    }
}