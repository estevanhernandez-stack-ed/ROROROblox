using ROROROblox.Core.Theming;
using D = ROROROblox.Core.Theming.EdgeRemediation.Decision;

namespace ROROROblox.Tests;

/// <summary>
/// Wave 5's consent rules. The contrast fix is not in question here — whether we change somebody
/// else's theme without asking is.
/// </summary>
public class EdgeRemediationTests
{
    private const string Navy = "#0F1F31";
    private const string FaintDivider = "#1F3149";   // 1.26:1 — the shipped defect
    private const string GoodDivider = "#5E6B7C";    // already clears 3:1

    [Fact]
    public void ABuiltInThemeIsFixedWithoutAsking()
    {
        // Every user is on a built-in by default. Prompting here would put a dialog in front of
        // everyone on first launch, about a defect they did not author.
        Assert.Equal(D.DeriveSilently,
            EdgeRemediation.Decide(isBuiltIn: true, Navy, FaintDivider, alreadyAnswered: false, declined: false));
    }

    [Fact]
    public void AUserThemeIsAskedAboutBeforeItChanges()
    {
        Assert.Equal(D.AskFirst,
            EdgeRemediation.Decide(isBuiltIn: false, Navy, FaintDivider, alreadyAnswered: false, declined: false));
    }

    [Fact]
    public void DecliningIsHonoured_TheThemeRendersAsAuthored()
    {
        var d = EdgeRemediation.Decide(isBuiltIn: false, Navy, FaintDivider, alreadyAnswered: true, declined: true);

        Assert.Equal(D.HonourDecline, d);
        // The point of asking is that "no" means no. If we derived anyway the dialog would be a
        // notification wearing a question's clothes.
        Assert.Equal(FaintDivider, EdgeRemediation.Resolve(d, Navy, FaintDivider));
    }

    [Fact]
    public void AcceptingIsRememberedAndNotReAsked()
    {
        var d = EdgeRemediation.Decide(isBuiltIn: false, Navy, FaintDivider, alreadyAnswered: true, declined: false);

        Assert.Equal(D.DeriveSilently, d);
        Assert.NotEqual(FaintDivider, EdgeRemediation.Resolve(d, Navy, FaintDivider));
    }

    [Fact]
    public void AThemeThatAlreadyPassesIsNeverMentioned()
    {
        // No dialog, no derivation, for either kind of theme. Someone who authored a compliant
        // theme should never learn this feature exists.
        foreach (var builtIn in new[] { true, false })
        {
            var d = EdgeRemediation.Decide(builtIn, Navy, GoodDivider, alreadyAnswered: false, declined: false);
            Assert.Equal(D.LeaveAlone, d);
            Assert.Equal(GoodDivider, EdgeRemediation.Resolve(d, Navy, GoodDivider));
        }
    }

    [Fact]
    public void WhileTheQuestionIsOpen_TheDerivedEdgeIsShown()
    {
        // The dialog asks whether to KEEP the change, so the change has to be on screen behind it.
        var resolved = EdgeRemediation.Resolve(D.AskFirst, Navy, FaintDivider);

        Assert.NotEqual(FaintDivider, resolved);
        Assert.True(ContrastGuard.RatioBetween(Navy, resolved) >= ContrastGuard.MinimumBoundaryRatio);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    public void AMalformedThemeIsNotAQuestionWorthAsking(string? divider)
    {
        Assert.Equal(D.LeaveAlone,
            EdgeRemediation.Decide(isBuiltIn: false, Navy, divider, alreadyAnswered: false, declined: false));
    }
}
