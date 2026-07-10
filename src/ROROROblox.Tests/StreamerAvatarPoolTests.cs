using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

public class StreamerAvatarPoolTests
{
    private static StreamerAvatarPool Pool(params string[] ids) => new(ids);

    [Fact]
    public void Next_AvoidsIdsInUse_WhenPossible()
    {
        var pool = Pool("noodle", "duck", "potato");
        var pick = pool.Next(new HashSet<string> { "noodle", "duck" });
        Assert.Equal("potato", pick);
    }

    [Fact]
    public void ResourceUri_BuildsPackUri()
    {
        var pool = Pool("noodle");
        Assert.Equal(
            "pack://application:,,,/ROROROblox.App;component/StreamerMode/Avatars/noodle.png",
            pool.ResourceUri("noodle"));
    }

    [Fact]
    public void Next_AllInUse_ReturnsAnId_NeverThrows()
    {
        var pool = Pool("noodle", "duck");
        var pick = pool.Next(new HashSet<string> { "noodle", "duck" });
        Assert.Contains(pick, new[] { "noodle", "duck" });
    }
}
