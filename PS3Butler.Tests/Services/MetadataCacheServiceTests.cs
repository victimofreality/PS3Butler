using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class MetadataCacheServiceTests
{
    [Fact]
    public void AddOrUpdate_AddsAndUpdatesCaseInsensitively()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = CreateService(temporaryDirectory);

        service.AddOrUpdate("Game Name", "BLUS00001", "Original Title");
        service.AddOrUpdate("game name", "BLUS00002", "Updated Title");

        var entries = service.Load();
        var entry = Assert.Single(entries);
        Assert.Equal("Game Name", entry.GameName);
        Assert.Equal("BLUS00002", entry.TitleId);
        Assert.Equal("Updated Title", entry.Title);
    }

    [Fact]
    public void FindByGameName_IsCaseInsensitive()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = CreateService(temporaryDirectory);
        service.AddOrUpdate("Demon's Souls", "BLUS30443", "Demon's Souls");

        var entry = service.FindByGameName("DEMON'S SOULS");

        Assert.NotNull(entry);
        Assert.Equal("BLUS30443", entry.TitleId);
    }

    [Fact]
    public void Load_ReturnsEmptyCollectionForCorruptCache()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var service = CreateService(temporaryDirectory);
        var parent = Path.GetDirectoryName(service.CacheFilePath)!;
        Directory.CreateDirectory(parent);
        File.WriteAllText(service.CacheFilePath, "{broken");

        var entries = service.Load();

        Assert.Empty(entries);
    }

    internal static MetadataCacheService CreateService(
        TemporaryDirectory temporaryDirectory)
    {
        var appDirectory = temporaryDirectory.CreateDirectory("app");
        var appDataDirectory = temporaryDirectory.CreateDirectory("appdata");
        var settingsService =
            new SettingsService(appDirectory, appDataDirectory);

        return new MetadataCacheService(settingsService);
    }
}
