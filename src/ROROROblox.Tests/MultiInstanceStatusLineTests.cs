using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// F-018 and F-034. The wording the app uses for its own core switch, pinned where it can be read
/// rather than spread across eight hand-copied literals.
/// </summary>
public class MultiInstanceStatusLineTests
{
    private static readonly MultiInstanceState[] AllStates =
        [MultiInstanceState.Off, MultiInstanceState.On, MultiInstanceState.Error];

    [Fact]
    public void EveryStateGetsItsOwnStatusBarLine()
    {
        // The shipped tray tooltip collapsed nothing, but the menu header did: Off and any unknown
        // value both fell through to "OFF". A footer that says "off" during an ERROR would be a
        // worse lie than saying nothing, because the user would stop looking for the problem.
        var lines = AllStates.Select(MultiInstanceStatusLine.StatusBar).ToList();

        Assert.Equal(3, lines.Distinct().Count());
    }

    [Fact]
    public void TheTooltipCarriesTheBrandAndNeverTheRepoName()
    {
        // The exact defect F-034 recorded: all three tooltip arms shipped "ROROROblox".
        foreach (var state in AllStates)
        {
            var tooltip = MultiInstanceStatusLine.Tooltip(state);
            Assert.Contains(Branding.ProductName, tooltip, StringComparison.Ordinal);
            Assert.DoesNotContain("ROROROblox", tooltip, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryStatusBarTooltipSaysWhereTheSwitchIs()
    {
        // The whole of F-018: the switch lives in a menu Windows hides by default. A user who has
        // never right-clicked the tray icon cannot discover it, so each arm has to say so — including
        // the ON arm, which is the one a user reads when nothing is wrong and everything is learnable.
        foreach (var state in AllStates)
        {
            Assert.Contains("tray", MultiInstanceStatusLine.StatusBarTooltip(state), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void TheErrorArmsSayWhatToDoAboutIt()
    {
        Assert.Contains("click to reload", MultiInstanceStatusLine.MenuHeader(MultiInstanceState.Error), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Multi-Instance", MultiInstanceStatusLine.StatusBarTooltip(MultiInstanceState.Error), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyOnIsHealthy()
    {
        Assert.True(MultiInstanceStatusLine.IsHealthy(MultiInstanceState.On));
        Assert.False(MultiInstanceStatusLine.IsHealthy(MultiInstanceState.Off));
        Assert.False(MultiInstanceStatusLine.IsHealthy(MultiInstanceState.Error));
    }

    [Fact]
    public void AFourthStateWouldNeedItsOwnWording()
    {
        // Every switch here ends in a default arm, which is right for a value that arrives off the
        // wire and wrong for one somebody adds on purpose: a new member would silently render as
        // "off" in the footer and read as a working product that is switched off. There is no way
        // to detect intent from a default arm, so this asserts the count instead and fails the
        // moment the enum grows — which is when the author is in a position to decide.
        Assert.Equal(3, Enum.GetValues<MultiInstanceState>().Length);
    }
}
