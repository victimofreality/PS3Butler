using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class FolderDeliveryServiceTests
{
    [Fact]
    public async Task CopyAsync_PreservesNestedDirectoryLayout()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var source = temporaryDirectory.CreateDirectory("source");
        temporaryDirectory.CreateTextFile(
            "source/PS3_GAME/USRDIR/EBOOT.BIN",
            "content");
        temporaryDirectory.CreateTextFile(
            "source/PS3_DISC.SFB",
            "metadata");
        var destination = temporaryDirectory.GetPath("destination");
        var reports = new List<TransferProgress>();
        var service = new FolderDeliveryService();

        var result = await service.CopyAsync(
            source,
            destination,
            new InlineProgress<TransferProgress>(reports.Add));

        Assert.True(result.Success, result.Error);
        Assert.Equal(destination, result.DestinationPath);
        Assert.Equal(
            "content",
            File.ReadAllText(
                Path.Combine(destination, "PS3_GAME", "USRDIR", "EBOOT.BIN")));
        Assert.Equal(
            "metadata",
            File.ReadAllText(Path.Combine(destination, "PS3_DISC.SFB")));
        Assert.NotEmpty(reports);
        Assert.Equal(100, reports[^1].Percentage);
    }

    [Fact]
    public async Task CopyAsync_ReturnsErrorForMissingSource()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new FolderDeliveryService();

        var result = await service.CopyAsync(
            temporaryDirectory.GetPath("missing"),
            temporaryDirectory.GetPath("destination"));

        Assert.False(result.Success);
        Assert.Equal("Source directory does not exist.", result.Error);
    }
}
