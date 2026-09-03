using PS3Butler.Configuration;
using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class SettingsServiceTests
{
    [Fact]
    public void Constructor_UsesProvidedAppDataDirectoryInNormalMode()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var appDirectory = temporaryDirectory.CreateDirectory("app");
        var appDataDirectory = temporaryDirectory.CreateDirectory("appdata");

        var service = new SettingsService(appDirectory, appDataDirectory);

        Assert.False(service.IsPortableMode);
        Assert.Equal(
            Path.Combine(appDataDirectory, "PS3Butler", "settings.json"),
            service.SettingsFilePath);
    }

    [Fact]
    public void SaveAndLoad_RoundTripsPortableSettings()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var appDirectory = temporaryDirectory.CreateDirectory("app");
        var appDataDirectory = temporaryDirectory.CreateDirectory("appdata");
        temporaryDirectory.CreateTextFile("app/portable.txt", string.Empty);

        var service = new SettingsService(appDirectory, appDataDirectory);
        var settings = new AppSettings
        {
            RedrrxPs3DecPath = "redrrx.exe",
            ClassicPs3DecPath = "classic.exe",
            SevenZipPath = "7z.exe",
            RedumpKeysPath = "keys",
            WorkDirectory = "work",
            IsoDestinationPath = "iso",
            JbDestinationPath = "jb",
            Ps3DecThreadCount = 12,
            RemovePs3Update = false
        };

        service.Save(settings);
        var loaded = service.Load();

        Assert.True(service.IsPortableMode);
        Assert.Equal(
            Path.Combine(appDirectory, "Data", "settings.json"),
            service.SettingsFilePath);
        Assert.Equal(settings.RedrrxPs3DecPath, loaded.RedrrxPs3DecPath);
        Assert.Equal(settings.ClassicPs3DecPath, loaded.ClassicPs3DecPath);
        Assert.Equal(settings.SevenZipPath, loaded.SevenZipPath);
        Assert.Equal(settings.RedumpKeysPath, loaded.RedumpKeysPath);
        Assert.Equal(settings.WorkDirectory, loaded.WorkDirectory);
        Assert.Equal(settings.IsoDestinationPath, loaded.IsoDestinationPath);
        Assert.Equal(settings.JbDestinationPath, loaded.JbDestinationPath);
        Assert.Equal(settings.Ps3DecThreadCount, loaded.Ps3DecThreadCount);
        Assert.Equal(settings.RemovePs3Update, loaded.RemovePs3Update);
    }

    [Fact]
    public void Load_ReturnsDefaultsForInvalidJson()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var appDirectory = temporaryDirectory.CreateDirectory("app");
        var appDataDirectory = temporaryDirectory.CreateDirectory("appdata");
        var service = new SettingsService(appDirectory, appDataDirectory);
        temporaryDirectory.CreateTextFile(
            "appdata/PS3Butler/settings.json",
            "not valid json");

        var loaded = service.Load();

        Assert.Equal(32, loaded.Ps3DecThreadCount);
        Assert.True(loaded.RemovePs3Update);
    }
}
