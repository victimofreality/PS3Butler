using PS3Butler.Models;

namespace PS3Butler.Tests.Models;

public class ToolStatusTests
{
    [Fact]
    public void StatusText_DescribesMissingAndCountedTools()
    {
        var missing = new ToolStatus
        {
            Path = "missing.exe",
            IsAvailable = false
        };
        var keys = new ToolStatus
        {
            Path = "keys",
            IsAvailable = true,
            ItemCount = 12
        };

        Assert.Equal("Not found: missing.exe", missing.StatusText);
        Assert.Contains("12 keys", keys.StatusText);
    }
}
