using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class DestinationInspectionServiceTests
{
    [Fact]
    public void ScanJbGames_ReadsValidGamesAndSkipsInvalidFolders()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = temporaryDirectory.CreateDirectory("games");
        temporaryDirectory.CreateFile(
            "games/Demon's Souls [BLUS30443]/PS3_GAME/PARAM.SFO",
            ParamSfoBuilder.Create("BLUS30443", "Demon's Souls"));
        temporaryDirectory.CreateFile(
            "games/Invalid/PS3_GAME/PARAM.SFO",
            [0x01, 0x02]);
        temporaryDirectory.CreateDirectory("games/Not A Game");
        var service = new DestinationInspectionService();

        var games = service.ScanJbGames(destination);

        var game = Assert.Single(games);
        Assert.Equal("BLUS30443", game.TitleId);
        Assert.Equal("Demon's Souls", game.Title);
        Assert.Equal("JB", game.Format);
    }

    [Fact]
    public void FindJbGameByTitleId_IsCaseInsensitive()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = temporaryDirectory.CreateDirectory("games");
        temporaryDirectory.CreateFile(
            "games/Game/PS3_GAME/PARAM.SFO",
            ParamSfoBuilder.Create("BLUS30443", "Game"));
        var service = new DestinationInspectionService();

        var game = service.FindJbGameByTitleId(destination, "blus30443");

        Assert.NotNull(game);
        Assert.Equal("Game", game.Title);
    }

    [Fact]
    public void FindExistingGame_NormalizesDecryptedIsoSuffix()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = temporaryDirectory.CreateDirectory("games");
        var isoPath = temporaryDirectory.CreateFile(
            "games/Game Name.decrypted.iso");
        var service = new DestinationInspectionService();

        var result = service.FindExistingGame(destination, "Game Name");

        Assert.Equal(isoPath, result);
    }

    [Fact]
    public void FindExistingGame_FindsAnExactlyNamedJbFolder()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = temporaryDirectory.CreateDirectory("games");
        var folder = temporaryDirectory.CreateDirectory(
            "games/Game Name/PS3_GAME");
        var service = new DestinationInspectionService();

        var result = service.FindExistingGame(destination, "Game Name");

        Assert.Equal(Directory.GetParent(folder)!.FullName, result);
    }

    [Fact]
    public void FindExistingEncryptedIsoWithKey_RequiresSidecarKey()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var destination = temporaryDirectory.CreateDirectory("games");
        var isoPath = temporaryDirectory.CreateFile("games/Game.iso");
        var service = new DestinationInspectionService();

        Assert.Null(
            service.FindExistingEncryptedIsoWithKey(destination, "Game"));

        temporaryDirectory.CreateTextFile("games/Game.dkey", "key");

        Assert.Equal(
            isoPath,
            service.FindExistingEncryptedIsoWithKey(destination, "Game"));
    }
}
