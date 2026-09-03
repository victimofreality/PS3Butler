using PS3Butler.Configuration;
using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class ToolDetectionServiceTests
{
    [Fact]
    public void DetectTools_ReportsFilesAndTopLevelKeyCount()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var redrrx = temporaryDirectory.CreateFile("tools/ps3decrs.exe");
        var classic = temporaryDirectory.CreateFile("tools/PS3Dec.exe");
        var sevenZip = temporaryDirectory.CreateFile("tools/7z.exe");
        var keys = temporaryDirectory.CreateDirectory("keys");
        temporaryDirectory.CreateTextFile("keys/one.dkey", "one");
        temporaryDirectory.CreateTextFile("keys/two.dkey", "two");
        temporaryDirectory.CreateTextFile("keys/ignored.txt", "ignored");
        var settings = new AppSettings
        {
            RedrrxPs3DecPath = redrrx,
            ClassicPs3DecPath = classic,
            SevenZipPath = sevenZip,
            RedumpKeysPath = keys
        };
        var service = new ToolDetectionService(settings);

        var tools = service.DetectTools();

        Assert.Equal(4, tools.Count);
        Assert.All(tools, tool => Assert.True(tool.IsAvailable));
        var keyStatus = Assert.Single(
            tools,
            tool => tool.Name == "Redump Keys");
        Assert.Equal(2, keyStatus.ItemCount);
    }
}
