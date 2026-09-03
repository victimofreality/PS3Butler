using PS3Butler.Models;

namespace PS3Butler.Tests.Models;

public class GameJobTests
{
    [Fact]
    public void DisplayName_ReturnsLocalFileName()
    {
        var job = new GameJob
        {
            SourcePath = Path.Combine("some", "folder", "Game.iso")
        };

        Assert.Equal("Game.iso", job.DisplayName);
    }

    [Fact]
    public void DisplayName_UnescapesUrlFileName()
    {
        var job = new GameJob
        {
            SourceType = GameSourceType.Url,
            SourcePath = "https://example.test/Game%20Name.iso"
        };

        Assert.Equal("Game Name.iso", job.DisplayName);
    }

    [Fact]
    public void EffectiveSourcePath_UsesDownloadedUrlPathWhenAvailable()
    {
        var job = new GameJob
        {
            SourceType = GameSourceType.Url,
            SourcePath = "https://example.test/game.iso",
            LocalSourcePath = "downloaded.iso"
        };

        Assert.Equal("downloaded.iso", job.EffectiveSourcePath);
    }

    [Fact]
    public void ObservableProperties_RaisePropertyChanged()
    {
        var job = new GameJob();
        var changedProperties = new List<string?>();
        job.PropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName);

        job.Status = "Ready";
        job.Progress = 50;

        Assert.Contains(nameof(GameJob.Status), changedProperties);
        Assert.Contains(nameof(GameJob.Progress), changedProperties);
    }
}
