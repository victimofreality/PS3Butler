using PS3Butler.Models;
using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class GamePreflightServiceTests
{
    private const string DiscKey = "00112233445566778899AABBCCDDEEFF";

    [Fact]
    public async Task PreflightAsync_MarksIsoReadyWhenMatchingKeyExists()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var source = temporaryDirectory.CreateFile("input/Game Name.iso");
        var keys = temporaryDirectory.CreateDirectory("keys");
        var keyPath = temporaryDirectory.CreateTextFile(
            "keys/Game Name.dkey",
            DiscKey);
        var service = CreateService(temporaryDirectory, keys);
        var job = new GameJob { SourcePath = source };

        await service.PreflightAsync(job);

        Assert.Equal("Ready", job.Status);
        Assert.Equal(DiscKey, job.DiscKey);
        Assert.Equal(keyPath, job.KeyPath);
        Assert.Equal("Game Name.iso", job.DetectedIsoName);
    }

    [Fact]
    public async Task PreflightAsync_MarksJobMissingKeyWhenNoMatchExists()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var source = temporaryDirectory.CreateFile("input/Game Name.iso");
        var keys = temporaryDirectory.CreateDirectory("keys");
        var service = CreateService(temporaryDirectory, keys);
        var job = new GameJob { SourcePath = source };

        await service.PreflightAsync(job);

        Assert.Equal("Missing key", job.Status);
        Assert.Null(job.DiscKey);
    }

    [Fact]
    public async Task PreflightAsync_DetectsExistingJbGameUsingCachedTitleId()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var source = temporaryDirectory.CreateFile(
            "input/Redump Filename.iso");
        var keys = temporaryDirectory.CreateDirectory("keys");
        var jbDestination = temporaryDirectory.CreateDirectory("jb");
        temporaryDirectory.CreateFile(
            "jb/Display Title [BLUS30443]/PS3_GAME/PARAM.SFO",
            ParamSfoBuilder.Create("BLUS30443", "Display Title"));
        var metadataCache =
            MetadataCacheServiceTests.CreateService(temporaryDirectory);
        metadataCache.AddOrUpdate(
            "Redump Filename",
            "BLUS30443",
            "Display Title");
        var service = CreateService(
            temporaryDirectory,
            keys,
            metadataCache,
            jbDestination);
        var job = new GameJob { SourcePath = source };

        await service.PreflightAsync(job);

        Assert.Equal("Already exists", job.Status);
        Assert.Equal("BLUS30443", job.TitleId);
        Assert.Contains("Display Title", job.Details);
    }

    [Fact]
    public async Task PreflightAsync_RejectsUnsupportedFiles()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var source = temporaryDirectory.CreateFile("input/Game.pkg");
        var keys = temporaryDirectory.CreateDirectory("keys");
        var service = CreateService(temporaryDirectory, keys);
        var job = new GameJob { SourcePath = source };

        await service.PreflightAsync(job);

        Assert.Equal("Error", job.Status);
        Assert.Contains("Unsupported file type", job.Details);
    }

    private static GamePreflightService CreateService(
        TemporaryDirectory temporaryDirectory,
        string keysDirectory,
        MetadataCacheService? metadataCache = null,
        string? jbDestination = null)
    {
        var keyIndex = new KeyIndexService();
        keyIndex.Build(keysDirectory);

        return new GamePreflightService(
            keyIndex,
            new ArchiveInspectionService(
                temporaryDirectory.GetPath("missing-7z.exe")),
            new DestinationInspectionService(),
            metadataCache ??
                MetadataCacheServiceTests.CreateService(temporaryDirectory),
            temporaryDirectory.CreateDirectory("iso"),
            jbDestination ?? temporaryDirectory.CreateDirectory("jb"));
    }
}
