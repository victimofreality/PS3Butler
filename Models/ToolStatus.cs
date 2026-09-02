namespace PS3Butler.Models;

public class ToolStatus
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public bool IsAvailable { get; set; }

    public int? ItemCount { get; set; }

    public string StatusText
    {
        get
        {
            if (!IsAvailable)
            {
                return $"Not found: {Path}";
            }

            if (ItemCount.HasValue)
            {
                return $"Found: {Path} ({ItemCount.Value:N0} keys)";
            }

            return $"Found: {Path}";
        }
    }
}