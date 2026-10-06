using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// v1.33 item 3 — exactly ONE code path may put something on a user's screen, and it is
/// <c>AlertDispatcher</c>.
/// <para>
/// WHAT BROKE. Three paths could. <c>AlertDispatcher</c> honoured destinations, the per-account
/// mute and the quiet period. <c>App.WireMemoryWarningTray</c> honoured none of the three: it
/// raised a Windows balloon on every memory crossing, so the measured outcome on 2026-10-05 was
/// the dispatcher logging "routed nowhere" under the cooldown while the balloon went out anyway.
/// <c>IdleAlertPresenter</c> honoured one mute flag and nothing else — no destinations, no cadence,
/// no per-account mute.
/// </para>
/// <para>
/// WHY A SOURCE SCAN. Neither of the two deleted producers is reachable from a test:
/// <c>App.xaml.cs</c> is a WPF <c>Application</c> and <c>WireMemoryWarningTray</c> is a private
/// method on it, and <c>TrayService</c> needs a real <c>TaskbarIcon</c>. The behaviour half of the
/// collapse IS unit-tested — see <c>IdleAlertTriggerTests</c> for the trigger the view model raises
/// and <c>AlertRouterTests</c> / <c>AlertDispatcherTests</c> for what the one surviving path does
/// with it. What cannot be tested that way is the absence of a SECOND producer, and absence is the
/// whole deliverable here: the PRD's "exactly one balloon per event, not two" is only checkable if
/// there is one thing that can produce one. Same justification
/// <c>TrayWiringTests.EveryBalloonTipInTrayService_IsRaisedOnTheUiThread</c> already runs on.
/// </para>
/// <para>
/// COMMENTS ARE STRIPPED, borrowing <c>SettingsReachabilityTests.WithoutComments</c> rather than
/// copying it. That file learned the lesson the hard way: a guard that reads prose as code reports
/// on something other than what it claims. It matters here in both directions — the doc comments on
/// <c>WireMemoryWarningTray</c> and <c>TrayService</c> still legitimately NAME
/// <c>ShowMemoryWarning</c> while explaining why nothing calls it.
/// </para>
/// </summary>
public class OnePathToTheScreenFenceTests
{
    /// <summary>
    /// Where the badge call lives, and the file this fence is mostly about.
    /// </summary>
    private const string CompositionRoot = "src/ROROROblox.App/App.xaml.cs";

    /// <summary>
    /// The tray itself may declare <c>ShowMemoryWarning</c>; the rule is that nothing CALLS it on
    /// the crossing path. Declaration sites are not invocations and are excluded by the pattern,
    /// not by this path — it is here only so a failure can say "except the tray".
    /// </summary>
    private const string TheTrayItself = "src/ROROROblox.App/Tray/TrayService.cs";

    /// <summary>
    /// An invocation THROUGH an object (<c>tray.ShowMemoryWarning(...)</c>), which is what a second
    /// producer looks like. Deliberately not a bare name match: <c>SettingsPage</c> has an
    /// unrelated private <c>ShowMemoryWarning(string)</c> of its own that paints an inline banner
    /// on the page, calls it unqualified, and has nothing to do with the tray.
    /// </summary>
    private static readonly Regex BalloonCall =
        new(@"\.\s*ShowMemoryWarning\s*\(", RegexOptions.Compiled);

    /// <summary>The badge — a colour change, not an interruption, and the half that survives.</summary>
    private static readonly Regex BadgeCall =
        new(@"\.\s*SetMemoryWarning\s*\(\s*true\s*\)", RegexOptions.Compiled);

    private static IReadOnlyList<(string RelativePath, string[] Code)> AppSources()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        var appDir = XamlStyleScanner.AppSourceDirectory();
        if (root is null || appDir is null) return [];

        var found = new List<(string, string[])>();
        foreach (var path in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            found.Add((relative, SettingsReachabilityTests.WithoutComments(relative, File.ReadAllLines(path))));
        }

        return found;
    }

    [Fact]
    public void TheMemoryCrossingStillColoursTheTrayBadge()
    {
        // The badge is what keeps memory pressure visible when every destination is off. It stays
        // unconditional and uncadenced on purpose: it interrupts nobody, so there is nothing for a
        // quiet period to protect, and gating it would mean a user who turned alerts off has no way
        // left to find out at all (PRD, "still find out").
        var app = AppSources().SingleOrDefault(f => f.RelativePath == CompositionRoot);
        Assert.NotNull(app.Code);

        Assert.True(app.Code.Any(l => BadgeCall.IsMatch(l)),
            $"{CompositionRoot} no longer sets the memory-warning tray badge. Item 3 removed the "
            + "BALLOON from the crossing path, not the badge — with every destination unticked the "
            + "badge is the only thing left that tells a user their machine is under pressure.");
    }

    [Fact]
    public void NothingCallsShowMemoryWarningAnyMore()
    {
        // The whole item, in one assertion. A memory crossing reaches a screen through exactly one
        // producer — the MemoryWarning trigger MainViewModel.BuildMemoryAlerts already raises,
        // dispatched like every other kind. A second call here is the 2026-10-05 defect returning:
        // a balloon that no destination, no mute and no cadence can stop.
        var offenders = new List<string>();

        foreach (var (relativePath, code) in AppSources())
        {
            for (var i = 0; i < code.Length; i++)
            {
                if (BalloonCall.IsMatch(code[i])) offenders.Add($"{relativePath}:{i + 1}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Something invokes ITrayService.ShowMemoryWarning. The memory crossing path may set the "
            + "tray badge and nothing else; the balloon belongs to AlertDispatcher, which is the "
            + "only place that knows about destinations, the per-account mute and the quiet period. "
            + $"Measured 2026-10-05: the dispatcher logged \"routed nowhere\" while the balloon from "
            + $"{CompositionRoot} went out anyway. ({TheTrayItself} may still DECLARE the method — "
            + "this fence matches invocations through an object, not declarations.)\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheIdlePresenterIsGone()
    {
        // The third path. Idle crossings now raise an AlertKind.AccountIdle trigger from
        // MainViewModel.ApplyActivityWarnCrossed and route like everything else; the presenter that
        // read one mute flag and went straight to the tray is deleted rather than taught the
        // routing rules a second time.
        var offenders = AppSources()
            .SelectMany(f => f.Code.Select((line, i) => (f.RelativePath, Line: line, Number: i + 1)))
            .Where(x => x.Line.Contains("IdleAlertPresenter", StringComparison.Ordinal))
            .Select(x => $"{x.RelativePath}:{x.Number}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "IdleAlertPresenter is still named in App code. It was the third path to the screen and "
            + "item 3 deletes it — a reference left behind means something still bypasses the "
            + "dispatcher's destinations, mute and cadence:\n  " + string.Join("\n  ", offenders));

        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        Assert.False(
            File.Exists(Path.Combine(root!, "src", "ROROROblox.App", "Notifications", "IdleAlertPresenter.cs")),
            "Notifications/IdleAlertPresenter.cs is still on disk. An unreferenced file is one "
            + "re-wiring away from being a producer again.");
    }

    [Fact]
    public void TheFenceSeesTheAppItClaimsTo()
    {
        // Vacuity floors ahead of the assertions above, because all three of them PASS on an empty
        // scan — "no offenders" is exactly what a broken walk reports. Same shape as the floors in
        // SettingsReachabilityTests and AccessibleNamingFenceTests.
        var files = AppSources();
        Assert.True(files.Count >= 60,
            $"The App walk found only {files.Count} .cs files; it should be seeing well over a "
            + "hundred. The repo-root discovery is broken, not the app — and every assertion in "
            + "this file would pass green on that.");

        Assert.Contains(CompositionRoot, files.Select(f => f.RelativePath));
        Assert.Contains(TheTrayItself, files.Select(f => f.RelativePath));

        // Both patterns must still fire on something, or the fence is measuring nothing. The badge
        // call proves BadgeCall works; the tray's own declaration proves the method still exists to
        // be called, so NothingCallsShowMemoryWarningAnyMore is green because nobody calls it rather
        // than because the name was spelled differently.
        Assert.True(files.Any(f => f.Code.Any(l => BadgeCall.IsMatch(l))),
            "The badge pattern matched nothing anywhere in the App. Either the call was renamed or "
            + "this regex stopped working; either way the fence above is vacuous.");

        var trayService = files.Single(f => f.RelativePath == TheTrayItself);
        Assert.True(trayService.Code.Any(l => l.Contains("ShowMemoryWarning", StringComparison.Ordinal)),
            "TrayService no longer spells ShowMemoryWarning at all. If the method was deleted, say "
            + "so here and in item 4's notes — this fence's point is that nothing CALLS it, which "
            + "is a different claim from it not existing.");
    }
}
