namespace PS3Butler.Models;

public class ToolStatus
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public string StatusText =>
        IsAvailable ? $"Found: {Path}" : $"Not found: {Path}";
}