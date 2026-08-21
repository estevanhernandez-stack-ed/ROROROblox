using ROROROblox.App.AppLifecycle;

namespace ROROROblox.Tests;

/// <summary>
/// F-110. The leftover dialog was firing on the ordinary path, because a clean stop deterministically
/// leaves a headless client behind and the gate counted it as an event.
/// </summary>
public class LeftoverStartupDecisionTests
{
    [Fact]
    public void TheShippedDefect_AHeadlessRemnantIsNotWorthADialog()
    {
        // Measured: stop a client cleanly, and ~2s later a fresh RobloxPlayerBeta exists with
        // hwnd=0 and no title. Every session. A dialog about that is a dialog about breathing.
        Assert.Equal(LeftoverDisposition.ClearSilently, LeftoverStartupDecision.Decide(windowless: 1, windowed: 0));
    }

    [Fact]
    public void AWindowedClientStillAsks()
    {
        // It has a window, so it may have a game in it. Sweeping that silently would be the
        // genuinely bad failure — worse than one dialog too many.
        Assert.Equal(LeftoverDisposition.Ask, LeftoverStartupDecision.Decide(windowless: 0, windowed: 1));
    }

    [Fact]
    public void OneWindowedAmongManyHeadlessStillAsks()
    {
        // The windowed one is the reason to ask; the headless ones do not outvote it.
        Assert.Equal(LeftoverDisposition.Ask, LeftoverStartupDecision.Decide(windowless: 5, windowed: 1));
    }

    [Fact]
    public void NothingLeftOverMeansNothingToDo()
    {
        Assert.Equal(LeftoverDisposition.Nothing, LeftoverStartupDecision.Decide(0, 0));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(9, 0)]
    public void ManyHeadlessRemnantsAreStillJustHousekeeping(int windowless, int windowed)
    {
        // They accumulate: one per clean stop, ~120-180 MB each, and nothing else sweeps them —
        // the tracker never knew them and F-103's sweeper skips empty titles on purpose.
        Assert.Equal(LeftoverDisposition.ClearSilently, LeftoverStartupDecision.Decide(windowless, windowed));
    }
}
