using PS3Butler.Services;
using PS3Butler.Tests.TestInfrastructure;

namespace PS3Butler.Tests.Services;

public class KeyIndexServiceTests
{
    [Fact]
    public void BuildAndFind_MatchesGameNamesCaseInsensitively()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var keysDirectory = temporaryDirectory.CreateDirectory("keys");
        var keyPath = temporaryDirectory.CreateTextFile(
            "keys/Game Name.dkey",
            "00112233445566778899AABBCCDDEEFF");
        var service = new KeyIndexService();

        service.Build(keysDirectory);

        Assert.Equal(1, service.Count);
        Assert.Equal(keyPath, service.Find("game name"));
    }

    [Fact]
    public void Build_ClearsThePreviousIndex()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstDirectory = temporaryDirectory.CreateDirectory("first");
        var secondDirectory = temporaryDirectory.CreateDirectory("second");
        temporaryDirectory.CreateTextFile(
            "first/Game.dkey",
            "00112233445566778899AABBCCDDEEFF");
        var service = new KeyIndexService();

        service.Build(firstDirectory);
        service.Build(secondDirectory);

        Assert.Equal(0, service.Count);
        Assert.Null(service.Find("Game"));
    }

    [Fact]
    public void TryReadKey_TrimsAndNormalizesHexText()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var keyPath = temporaryDirectory.CreateTextFile(
            "game.dkey",
            "  00112233445566778899aabbccddeeff\r\n");

        var success = KeyIndexService.TryReadKey(
            keyPath,
            out var key,
            out var error);

        Assert.True(success);
        Assert.Equal("00112233445566778899AABBCCDDEEFF", key);
        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("0011", "expected 32")]
    [InlineData("00112233445566778899AABBCCDDEEFG", "non-hexadecimal")]
    public void TryReadKey_RejectsInvalidText(
        string contents,
        string expectedError)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var keyPath = temporaryDirectory.CreateTextFile("game.dkey", contents);

        var success = KeyIndexService.TryReadKey(
            keyPath,
            out var key,
            out var error);

        Assert.False(success);
        Assert.Equal(string.Empty, key);
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void TryReadKey_RejectsMissingFile()
    {
        var success = KeyIndexService.TryReadKey(
            "missing.dkey",
            out var key,
            out var error);

        Assert.False(success);
        Assert.Equal(string.Empty, key);
        Assert.Equal("Key file does not exist.", error);
    }
}
