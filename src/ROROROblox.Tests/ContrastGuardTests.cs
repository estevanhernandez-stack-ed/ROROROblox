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
        // Theme JSON in the wild carries #AARRGGBB as well as #RRGGBB. Fully opaque, so the two
        // forms must measure identically.
        Assert.Equal(ContrastGuard.RatioBetween("#0F1F31", "#17D4FA"),
                     ContrastGuard.RatioBetween("#FF0F1F31", "#FF17D4FA"));
    }

    [Fact]
    public void ShorthandHexIsAccepted()
    {
        // #abc is #aabbcc by digit doubling — the rule CSS and WPF both use. Before this, shorthand
        // returned null, which read to the caller as "nothing to fix" and skipped the theme
        // entirely. Found by the wave-5 review gate.
        Assert.Equal(ContrastGuard.RatioBetween("#0F1F31", "#AABBCC"),
                     ContrastGuard.RatioBetween("#0F1F31", "#ABC"));
    }

    [Fact]
    public void ATranslucentBoundaryIsMeasuredAgainstWhatIsActuallyBehindIt()
    {
        // #20FFFFFF is a 12%-alpha white hairline — a common way to draw a faint edge. Dropping the
        // alpha byte measured it at 16.66:1 and left the theme alone; composited onto brand navy it
        // is really about 1.5:1 and needs the fix like any other faint edge.
        var measured = ContrastGuard.RatioBetween("#0F1F31", "#20FFFFFF");

        Assert.NotNull(measured);
        Assert.True(measured < 2.0, $"expected a faint translucent hairline, measured {measured:0.00}:1");
        Assert.True(ContrastGuard.RatioBetween("#0F1F31", ContrastGuard.Ensure("#0F1F31", "#20FFFFFF"))
                    >= ContrastGuard.MinimumBoundaryRatio);
    }

    [Fact]
    public void WhatItReturnsIsWhatItMeasured_AcrossTheWholeColourSpace()
    {
        // THE BUG THIS TEST EXISTS FOR (wave-5 review gate, 2026-08-05): the loop checked the ratio
        // of an un-quantized double triple, then returned ToHex(...), which rounds to bytes. The
        // rounding could push the returned colour back under 3:1 — 9,569 of 147,511 failing pairs,
        // about one in fifteen. All three built-ins cleared it by luck (midnight by 0.019), so the
        // per-theme tests above stayed green while the guarantee was false for user themes.
        //
        // Deterministic, not random: a fixed sweep over the colour space, so a regression fails the
        // same way on every machine and every run.
        var offenders = new List<string>();

        for (var r = 0; r < 256; r += 17)
        for (var g = 0; g < 256; g += 17)
        for (var b = 0; b < 256; b += 17)
        {
            var surface = $"#{r:X2}{g:X2}{b:X2}";

            // The shape every real theme has: a divider a hair off the surface it sits on.
            foreach (var delta in new[] { 0, 8, 16 })
            {
                var candidate = $"#{Math.Min(255, r + delta):X2}{Math.Min(255, g + delta):X2}{Math.Min(255, b + delta):X2}";
                var guarded = ContrastGuard.Ensure(surface, candidate);
                var ratio = ContrastGuard.RatioBetween(surface, guarded);

                if (ratio < ContrastGuard.MinimumBoundaryRatio)
                {
                    offenders.Add($"{surface} + {candidate} -> {guarded} = {ratio:0.0000}:1");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} surfaces got a boundary below the 3:1 floor the guard promises:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders.Take(10)));
    }

    [Fact]
    public void TheBuiltInThemesAreUnmovedByTheRoundingFix()
    {
        // Snapping before the check must not repaint anything already shipped. These three strings
        // are what the guard returned before the fix; they must still be exactly what it returns.
        Assert.Equal("#5E6B7C", ContrastGuard.Ensure("#0F1F31", "#1F3149"));
        Assert.Equal("#5A626D", ContrastGuard.Ensure("#0A1320", "#162232"));
        Assert.Equal("#6C5D70", ContrastGuard.Ensure("#1A0F1F", "#2D1832"));
    }
}
