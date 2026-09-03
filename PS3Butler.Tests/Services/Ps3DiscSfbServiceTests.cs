using System.Text;
using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class Ps3DiscSfbServiceTests
{
    [Theory]
    [InlineData("TITLE_ID=BLUS-30443", "BLUS30443")]
    [InlineData("product BCES 00001 data", "BCES00001")]
    public void ReadTitleId_NormalizesRecognizedTitleIds(
        string contents,
        string expected)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = temporaryDirectory.CreateFile(
            "PS3_DISC.SFB",
            Encoding.ASCII.GetBytes(contents));
        var service = new Ps3DiscSfbService();

        Assert.Equal(expected, service.ReadTitleId(path));
    }

    [Fact]
    public void ReadTitleId_ReturnsNullWhenNoTitleIdExists()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = temporaryDirectory.CreateTextFile(
            "PS3_DISC.SFB",
            "no product identifier");
        var service = new Ps3DiscSfbService();

        Assert.Null(service.ReadTitleId(path));
    }
}
