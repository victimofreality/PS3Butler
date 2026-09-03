using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class ParamSfoServiceTests
{
    [Fact]
    public void Read_ReturnsTitleMetadataFromValidSfo()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var sfoPath = temporaryDirectory.CreateFile(
            "PARAM.SFO",
            ParamSfoBuilder.Create("BLUS30443", "Demon's Souls"));
        var service = new ParamSfoService();

        var metadata = service.Read(sfoPath);

        Assert.NotNull(metadata);
        Assert.Equal("BLUS30443", metadata.TitleId);
        Assert.Equal("Demon's Souls", metadata.Title);
    }

    [Fact]
    public void Read_ReturnsNullForInvalidOrTruncatedSfo()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var invalidPath = temporaryDirectory.CreateFile(
            "invalid.sfo",
            [0x00, 0x01, 0x02]);
        var service = new ParamSfoService();

        Assert.Null(service.Read(invalidPath));
        Assert.Null(service.Read(temporaryDirectory.GetPath("missing.sfo")));
    }
}
