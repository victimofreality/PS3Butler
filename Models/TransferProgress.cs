using System;

namespace PS3Butler.Models;

public class TransferProgress
{
    public double Percentage { get; set; }

    public double MegabytesPerSecond { get; set; }

    public TimeSpan? EstimatedRemaining { get; set; }
}