using ROROROblox.App.Friends;

namespace ROROROblox.Tests;

/// <summary>
/// The pure decision for which friends-list sources the picker offers and which is the default.
/// Main (when present and a DIFFERENT account than the opened row) is index 0 = shown by default;
/// the opened row is always a source. Main == row, or no main, collapses to single-source.
/// </summary>
public class FriendSourcePlanTests
{
    [Fact]
    public void Build_MainPresentAndDistinct_MainIsDefaultThenRow()
    {
        var row = new FriendSource(Guid.NewGuid(), 100, "Alt", IsMain: false);
        var main = new FriendSource(Guid.NewGuid(), 200, "MainGuy", IsMain: true);

        var (sources, defaultIndex) = FriendSourcePlan.Build(row, main);

        Assert.Equal(2, sources.Count);
        Assert.Same(main, sources[0]);   // index 0 = main = shown by default
        Assert.Same(row, sources[1]);
        Assert.Equal(0, defaultIndex);
    }

    [Fact]
    public void Build_NoMain_SingleSourceIsTheRow()
    {
        var row = new FriendSource(Guid.NewGuid(), 100, "Alt", IsMain: false);

        var (sources, defaultIndex) = FriendSourcePlan.Build(row, main: null);

        Assert.Same(row, Assert.Single(sources));
        Assert.Equal(0, defaultIndex);
    }

    [Fact]
    public void Build_MainIsTheOpenedRow_SingleSource()
    {
        var id = Guid.NewGuid();
        var row = new FriendSource(id, 100, "MainGuy", IsMain: true);
        var main = new FriendSource(id, 100, "MainGuy", IsMain: true);

        var (sources, defaultIndex) = FriendSourcePlan.Build(row, main);

        Assert.Same(row, Assert.Single(sources));
        Assert.Equal(0, defaultIndex);
    }
}
