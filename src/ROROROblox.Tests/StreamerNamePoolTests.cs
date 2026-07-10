using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

public class StreamerNamePoolTests
{
    private static StreamerNamePool Pool(params string[] names) => new(names);

    [Fact]
    public void Next_AvoidsNamesInUse_WhenPossible()
    {
        var pool = Pool("CaptainNoodle", "SirRerollington", "LadyPixel");
        var used = new HashSet<string> { "CaptainNoodle", "SirRerollington" };

        var pick = pool.Next(used);

        Assert.Equal("LadyPixel", pick);
    }

    [Fact]
    public void Next_AllInUse_StillReturnsAName_NeverThrows()
    {
        var pool = Pool("CaptainNoodle", "SirRerollington");
        var used = new HashSet<string> { "CaptainNoodle", "SirRerollington" };

        var pick = pool.Next(used);

        Assert.Contains(pick, new[] { "CaptainNoodle", "SirRerollington" });
    }

    [Fact]
    public void EmbeddedList_LoadsAtLeast50Names()
    {
        var pool = new StreamerNamePool();
        Assert.True(pool.Count >= 50, $"expected >=50 bundled names, got {pool.Count}");
    }
}
