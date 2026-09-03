using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class DeliveryServiceTests
{
    [Fact]
    public async Task CopyAsync_CopiesAndRenamesFileWithProgress()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var contents = Enumerable.Range(0, 2048)
            .Select(value => (byte)(value % 251))
            .ToArray();
        var source = temporaryDirectory.CreateFile("source.iso", contents);
        var destination = temporaryDirectory.CreateDirectory("destination");
        var reports = new List<TransferProgress>();
        var service = new DeliveryService();

        var result = await service.CopyAsync(
            source,
            destination,
            new InlineProgress<TransferProgress>(reports.Add),
            "renamed.iso");

        Assert.True(result.Success, result.Error);
        Assert.Equal(
            Path.Combine(destination, "renamed.iso"),
            result.DestinationPath);
        Assert.Equal(contents, File.ReadAllBytes(result.DestinationPath));
        Assert.NotEmpty(reports);
        Assert.Equal(100, reports[^1].Percentage);
    }

    [Fact]
    public async Task CopyAsync_ReturnsErrorForMissingSource()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = new DeliveryService();

        var result = await service.CopyAsync(
            temporaryDirectory.GetPath("missing.iso"),
            temporaryDirectory.GetPath("destination"));

        Assert.False(result.Success);
        Assert.Equal("Source file does not exist.", result.Error);
    }
}
