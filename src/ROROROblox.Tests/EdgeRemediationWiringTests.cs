using System.IO;
using ROROROblox.App.Theming;
using ROROROblox.Core;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

/// <summary>
/// Wave 5, stage 4b — the parts of the remediation prompt that are not the dialog itself: does an
/// answer survive to the next launch, and does the right theme raise the question.
/// <para>
/// <see cref="EdgeRemediationTests"/> covers the rules. These cover the wiring, because a rule that
/// says "never ask twice" is worth nothing if the answer is not actually written down.
/// </para>
/// </summary>
public class EdgeRemediationWiringTests : IDisposable
{
    private const string Navy = "#0F1F31";
    private const string FaintDivider = "#1F3149";   // 1.26:1 — below the 3:1 floor

    /// <summary>
    /// Clears 3:1 on BOTH surfaces this theme puts a control on: 3.26:1 against <see cref="Navy"/>
    /// and 3.06:1 against its <c>RowBg</c> of <c>#152438</c>.
    /// <para>
    /// It was <c>#5E6B7C</c> until F-090, and that value was named "good" because it measures
    /// 3.07:1 against Navy — while measuring <b>2.89:1 against the same theme's cards</b>. So the
    /// fixture standing for "a theme with nothing wrong with it" had the exact defect F-090
    /// describes, and the test asserting such a theme is never questioned was asserting that a
    /// theme which SHOULD be questioned is not. The finding was inside its own control group.
    /// </para>
    /// </summary>
    private const string GoodDivider = "#626F80";

    /// <summary>Clears Navy (3.07:1) and fails the card (2.89:1) — the F-090 shape, kept as a
    /// fixture now that it is no longer mistaken for a compliant theme.</summary>
    private const string NavyOnlyDivider = "#5E6B7C";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rororo-edge-" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AThemeNobodyHasBeenAskedAboutHasNoAnswer()
    {
        using var settings = new AppSettings(SettingsPath);

        Assert.Null(await settings.GetEdgeRemediationAnswerAsync("someones-theme"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnAnswerSurvivesARestart(bool accepted)
    {
        // The whole point of asking once is that the app remembers. A separate AppSettings over the
        // same file is what the next launch actually does.
        using (var first = new AppSettings(SettingsPath))
        {
            await first.SetEdgeRemediationAnswerAsync("someones-theme", accepted);
        }

        using var next = new AppSettings(SettingsPath);
        Assert.Equal(accepted, await next.GetEdgeRemediationAnswerAsync("someones-theme"));
    }

    [Fact]
    public async Task AnswersAreKeptPerTheme()
    {
        // Answering about one theme must not silence the question for a different one — they are
        // different people's work, or at least different decisions.
        using var settings = new AppSettings(SettingsPath);
        await settings.SetEdgeRemediationAnswerAsync("theme-a", accepted: false);
        await settings.SetEdgeRemediationAnswerAsync("theme-b", accepted: true);

        Assert.False(await settings.GetEdgeRemediationAnswerAsync("theme-a"));
        Assert.True(await settings.GetEdgeRemediationAnswerAsync("theme-b"));
        Assert.Null(await settings.GetEdgeRemediationAnswerAsync("theme-c"));
    }

    [Fact]
    public async Task AnAnswerCanBeChangedLater()
    {
        // Not reachable from the dialog today (it asks once), but the store must not be write-once —
        // a "re-ask me" affordance in Settings would land on exactly this call.
        using var settings = new AppSettings(SettingsPath);
        await settings.SetEdgeRemediationAnswerAsync("someones-theme", accepted: true);
        await settings.SetEdgeRemediationAnswerAsync("someones-theme", accepted: false);

        Assert.False(await settings.GetEdgeRemediationAnswerAsync("someones-theme"));
    }

    [Fact]
    public async Task EveryOtherSettingSurvivesAnAnswerBeingWritten()
    {
        // EdgeRemediationAnswers joined a record that eleven other settings share. Writing one must
        // not clear the rest — the failure mode of a blob-of-JSON settings file.
        using var settings = new AppSettings(SettingsPath);
        await settings.SetActiveThemeIdAsync("someones-theme");
        await settings.SetStreamerModeAsync(true);
        await settings.SetIdleWarnThresholdMinutesAsync(42);

        await settings.SetEdgeRemediationAnswerAsync("someones-theme", accepted: true);

        Assert.Equal("someones-theme", await settings.GetActiveThemeIdAsync());
        Assert.True(await settings.GetStreamerModeAsync());
        Assert.Equal(42, await settings.GetIdleWarnThresholdMinutesAsync());
    }

    [Fact]
    public void OnlyAskFirstProducesAQuestion()
    {
        var theme = UserTheme(FaintDivider);

        Assert.NotNull(ThemeService.QuestionFor(theme, EdgeRemediation.Decision.AskFirst));
        Assert.Null(ThemeService.QuestionFor(theme, EdgeRemediation.Decision.DeriveSilently));
        Assert.Null(ThemeService.QuestionFor(theme, EdgeRemediation.Decision.HonourDecline));
        Assert.Null(ThemeService.QuestionFor(theme, EdgeRemediation.Decision.LeaveAlone));
    }

    [Fact]
    public void TheQuestionCarriesBothColoursSoTheDialogCanShowThem()
    {
        // A dialog that only describes a colour change in words asks somebody to decide about
        // something they cannot see.
        var question = ThemeService.QuestionFor(UserTheme(FaintDivider), EdgeRemediation.Decision.AskFirst);

        Assert.NotNull(question);
        Assert.Equal("someones-theme", question!.ThemeId);
        Assert.Equal(Navy, question.Surface);
        Assert.Equal(FaintDivider, question.AuthoredEdge);
        Assert.NotEqual(FaintDivider, question.DerivedEdge);
        Assert.True(ContrastGuard.RatioBetween(Navy, question.DerivedEdge) >= ContrastGuard.MinimumBoundaryRatio);
    }

    [Fact]
    public void ACompliantThemeIsNeverTheSubjectOfAQuestion()
    {
        // Belt and braces on the rule that matters most for a stranger's theme: no shortfall, no
        // question. Decide() already returns LeaveAlone here; this pins the pairing.
        var theme = UserTheme(GoodDivider);
        var decision = EdgeRemediation.Decide(
            theme.IsBuiltIn, [theme.Navy, theme.RowBg], theme.Divider, alreadyAnswered: false, declined: false);

        Assert.Equal(EdgeRemediation.Decision.LeaveAlone, decision);
        Assert.Null(ThemeService.QuestionFor(theme, decision));
    }

    [Fact]
    public void AThemeThatOnlyPassesOnTheWindowFieldIsStillAsked()
    {
        // F-090 at the consent layer. `#5E6B7C` clears 3:1 against Navy and lands at 2.89:1 on this
        // theme's cards, where eight of the fourteen input call sites actually sit — so before the
        // fix Decide() said LeaveAlone and the shortfall was never derived away and never raised.
        // A boundary that fails on one of the two surfaces it lands on is not a compliant boundary.
        var theme = UserTheme(NavyOnlyDivider);
        var decision = EdgeRemediation.Decide(
            theme.IsBuiltIn, [theme.Navy, theme.RowBg], theme.Divider, alreadyAnswered: false, declined: false);

        Assert.Equal(EdgeRemediation.Decision.AskFirst, decision);
        Assert.NotNull(ThemeService.QuestionFor(theme, decision));
    }

    private static Theme UserTheme(string divider) => new(
        Id: "someones-theme",
        Name: "Someone's theme",
        Bg: Navy,
        Cyan: "#17D4FA",
        Magenta: "#F22F89",
        White: "#FFFFFF",
        MutedText: "#93A3B8",
        Divider: divider,
        RowBg: "#152438",
        RowExpiredBg: "#2A1520",
        RowExpiredAccent: "#F22F89",
        Navy: Navy,
        IsBuiltIn: false);
}
