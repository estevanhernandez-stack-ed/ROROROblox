using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

/// <summary>
/// F-031. A secondary button's edge measures ~1.2:1 against WCAG 1.4.11's 3:1 floor in all three
/// built-in themes — including the one that ships by default. These tests pin the guarantee that
/// replaces it, and the last one is the check whose absence let this ship for years.
/// </summary>
public class ContrastGuardTests
{
    // Verbatim from ThemeStore.cs:202-250. Navy == Bg in every one of them, which is why the
    // secondary fill contributes nothing and the boundary is doing all the work alone.
    public static TheoryData<string, string, string> BuiltInThemes() => new()
    {
        { "brand",        "#0F1F31", "#1F3149" },
        { "midnight",     "#0A1320", "#162232" },
        { "magenta-heat", "#1A0F1F", "#2D1832" },
    };

    [Theory]
    [MemberData(nameof(BuiltInThemes))]
    public void EveryBuiltInTheme_GetsABoundaryThatClearsTheFloor(string name, string navy, string divider)
    {
        // The check that did not exist. Had it, F-031 would have failed the build on the day the
        // brand theme was authored instead of surviving into shipped releases.
        var before = ContrastGuard.RatioBetween(navy, divider);
        Assert.NotNull(before);
        Assert.True(before < ContrastGuard.MinimumBoundaryRatio,
            $"{name} was expected to FAIL before guarding — if this trips, the theme changed and this test's premise is stale.");

        var guarded = ContrastGuard.Ensure(navy, divider);
        var after = ContrastGuard.RatioBetween(navy, guarded);

        Assert.NotNull(after);
        Assert.True(after >= ContrastGuard.MinimumBoundaryRatio,
            $"{name}: guarded boundary {guarded} reaches only {after:F2}:1 against {navy}.");
    }

    [Fact]
    public void AlreadyLegibleBoundaries_AreLeftAlone()
    {
        // The primary recipe passes at 9.39:1 in brand. Nudging it would make the app louder to fix
        // a problem it does not have.
        const string navy = "#0F1F31", cyan = "#17D4FA";
        Assert.Equal(cyan, ContrastGuard.Ensure(navy, cyan));
    }

    [Fact]
    public void ALightFieldDarkensTheBoundaryInsteadOfLightening()
    {
        // Direction comes from the surface, not the candidate. A user theme on a pale field would
        // otherwise get a boundary pushed toward white — away from legibility, not toward it.
        var guarded = ContrastGuard.Ensure("#F5F5F5", "#EEEEEE");
        var ratio = ContrastGuard.RatioBetween("#F5F5F5", guarded);

        Assert.NotNull(ratio);
        Assert.True(ratio >= ContrastGuard.MinimumBoundaryRatio, $"got {guarded} at {ratio:F2}:1");
    }

    [Fact]
    public void TheDegenerateThemeStillGetsAnEdge()
    {
        // The flatline case, and the one a careless user theme reaches: surface and boundary set to
        // the same value. Zero information to work from, and it still has to produce an edge.
        var guarded = ContrastGuard.Ensure("#22202A", "#22202A");
        var ratio = ContrastGuard.RatioBetween("#22202A", guarded);

        Assert.NotEqual("#22202A", guarded);
        Assert.True(ratio >= ContrastGuard.MinimumBoundaryRatio, $"got {guarded} at {ratio:F2}:1");
    }

    [Fact]
    public void ItDoesNotOvershoot()
    {
        // Clearing the floor is the job; blowing past it would make every secondary control shout
        // louder than the primary CTA, which is a different design bug traded for this one.
        var guarded = ContrastGuard.Ensure("#0F1F31", "#1F3149");
        var ratio = ContrastGuard.RatioBetween("#0F1F31", guarded)!.Value;

        Assert.InRange(ratio, ContrastGuard.MinimumBoundaryRatio, ContrastGuard.MinimumBoundaryRatio + 0.5);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-colour")]
    [InlineData("#12345")]
    public void MalformedInputDegradesInsteadOfThrowing(string? bad)
    {
        // This runs on a theme-switch path. A user theme with one bad value should look wrong, not
        // take the window down.
        var ex = Record.Exception(() => ContrastGuard.Ensure("#0F1F31", bad));
        Assert.Null(ex);
        Assert.Null(ContrastGuard.RatioBetween("#0F1F31", bad));
    }

    [Fact]
    public void EightDigitHexIsAccepted()
    {
        // Theme JSON in the wild carries #AARRGGBB as well as #RRGGBB.
        Assert.Equal(ContrastGuard.RatioBetween("#0F1F31", "#17D4FA"),
                     ContrastGuard.RatioBetween("#FF0F1F31", "#FF17D4FA"));
    }
}
