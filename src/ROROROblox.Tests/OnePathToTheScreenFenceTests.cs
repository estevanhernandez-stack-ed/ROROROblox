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
/// <c>ShowMemoryWarning</c> while explaining why it is gone.
/// </para>
/// <para>
/// UPDATED 2026-10-06 by item 4: <c>ShowMemoryWarning</c> no longer EXISTS. Item 3 left the method,
/// <c>_lastMemoryWarningAccountId</c> and the shell-balloon click handler standing because deleting
/// them would have deleted the app's only click-to-focus path, and said so; item 4 replaced that
/// path — the account rides on the drawn <c>AlertBalloon</c> now — and the chain went with the shell
/// balloon it wrapped. So <see cref="TheFenceSeesTheAppItClaimsTo"/> asserts the opposite of what it
/// used to, and <see cref="BalloonCall"/> is proven against a sample rather than against the tray's
/// own declaration.
/// </para>
/// </summary>
public class OnePathToTheScreenFenceTests
{
    /// <summary>
    /// Where the badge call lives, and the file this fence is mostly about.
    /// </summary>
    private const string CompositionRoot = "src/ROROROblox.App/App.xaml.cs";

    /// <summary>
    /// The tray. Item 3's rule was that it MAY declare <c>ShowMemoryWarning</c> so long as nothing
    /// calls it on the crossing path; item 4 deleted the declaration too, so this path is now where
    /// <see cref="TheFenceSeesTheAppItClaimsTo"/> goes to check the method is really gone rather
    /// than renamed.
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

        // BadgeCall must still fire on something, or the badge clause is measuring nothing. (The
        // companion proof for BalloonCall used to be the tray's own declaration of the method —
        // item 4 deleted it, so that pattern is exercised against a sample below instead.)
        Assert.True(files.Any(f => f.Code.Any(l => BadgeCall.IsMatch(l))),
            "The badge pattern matched nothing anywhere in the App. Either the call was renamed or "
            + "this regex stopped working; either way the fence above is vacuous.");

        // SAYING SO, as this clause's previous text asked for. It read: "TrayService no longer
        // spells ShowMemoryWarning at all. If the method was deleted, say so here and in item 4's
        // notes — this fence's point is that nothing CALLS it, which is a different claim from it
        // not existing." It was deleted, on 2026-10-06, by v1.33 item 4, along with
        // _lastMemoryWarningAccountId and the TrayBalloonTipClicked subscription that replayed it.
        // Item 3 left all three standing precisely because deleting them would have deleted the
        // only click-to-focus path in the app; item 4 replaced that path — the account now rides on
        // the drawn balloon — so the reason expired and the chain went.
        //
        // The claim therefore changes shape here: it is no longer "nothing calls a method that
        // exists" but "the method does not exist", which is the stronger of the two and needs no
        // allow-list. TrayWiringTests.TrayServiceKeepsNoRememberedAccountForABalloonClick holds the
        // rest of the chain down by name.
        var trayService = files.Single(f => f.RelativePath == TheTrayItself);
        Assert.False(trayService.Code.Any(l => l.Contains("ShowMemoryWarning", StringComparison.Ordinal)),
            "TrayService spells ShowMemoryWarning again. The method, the remembered account id and "
            + "the shell-balloon click handler were deleted together by v1.33 item 4 — the account a "
            + "notification is about now arrives WITH it and rides on the AlertBalloon instance, so "
            + "there is nothing for this method to be the one writer of.");

        // WHICH LEAVES BalloonCall UNEXERCISED, and an unexercised regex is a fence that measures
        // nothing. Until item 4 the tray's own declaration proved the name was still spelled the
        // way the pattern expects; with the declaration gone, the pattern is proven against a
        // sample instead — a change in form, not in strength, because what it has to catch is an
        // invocation and there has never been one of those to match.
        Assert.True(BalloonCall.IsMatch("tray.ShowMemoryWarning(title, body, accountId);"),
            "The BalloonCall pattern no longer matches an invocation through an object, so "
            + "NothingCallsShowMemoryWarningAnyMore is green because the regex broke rather than "
            + "because nothing calls it.");
        Assert.False(BalloonCall.IsMatch("ShowMemoryWarning(message);"),
            "The BalloonCall pattern now matches an UNQUALIFIED call. SettingsPage has a private "
            + "ShowMemoryWarning(string) of its own that paints an inline banner on the page and has "
            + "nothing to do with the tray; matching it would make this fence fail on unrelated code.");
    }

    // ---- v1.33 item 5: one event is one sound -------------------------------------------------
    //
    // The same thesis one layer down. Item 3 collapsed three producers of a BALLOON into one; this
    // keeps the SOUND attached to that one producer rather than to the thing above it. The
    // dispatcher fans one event out to Local, the clan channel, a webhook and a phone, sequentially
    // in a loop — a sound played there would play per DESTINATION, so a user with three ticked
    // would hear three chimes for one memory crossing. Only the desktop leg makes a noise, and the
    // desktop leg is the drawn balloon's show path.

    /// <summary>The only file allowed to end in an audio API, and the only file that plays one.</summary>
    private const string TheSoundPlayer = "src/ROROROblox.App/Tray/AlertSoundPlayer.cs";

    /// <summary>
    /// Where the sound must come from. Anything that reaches an audio API here or in the player is
    /// fine; anything that reaches one anywhere else is a second producer.
    /// </summary>
    private static readonly Regex AudioApi =
        new(@"\b(SoundPlayer|SystemSounds)\b", RegexOptions.Compiled);

    [Fact]
    public void TheSoundIsPlayedFromTheBalloonsShowPath()
    {
        var tray = AppSources().SingleOrDefault(f => f.RelativePath == TheTrayItself);
        Assert.NotNull(tray.Code);
        var source = string.Join('\n', tray.Code);

        const string method = "public void ShowToast(";
        var start = source.IndexOf(method, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{method} is gone from TrayService — this fence needs updating.");

        var body = source[start..];
        var marshal = body.IndexOf("Dispatcher.Invoke", StringComparison.Ordinal);
        Assert.True(marshal >= 0, $"{method} no longer marshals; TrayWiringTests says why that matters.");

        // INSIDE the marshal, bracket-balanced, for the reason item 4 learned the hard way: a
        // position-in-text assertion is satisfied by moving the statement onto the next line.
        var span = BalancedSpan(body, marshal);
        Assert.Contains("ShowCustomBalloon", span, StringComparison.Ordinal);
        Assert.Contains(".Play()", span,  StringComparison.Ordinal);

        // ONE sound per balloon shown, not one per call site. Two Play() calls in this file would
        // mean one event could make two noises, which is the shape this fence exists to refuse.
        Assert.Equal(1, source.Split(".Play()").Length - 1);
    }

    [Fact]
    public void NothingOutsideTheSoundPlayerTouchesAnAudioApi()
    {
        // The dispatcher is the one named in the spec, and it is the one that would be wrong in the
        // most expensive way: its fan-out loop is sequential, so a sound played per destination
        // means a user with Local + clan + phone ticked hears three chimes for one crossing.
        var offenders = new List<string>();

        foreach (var (relativePath, code) in AppSources())
        {
            if (relativePath == TheSoundPlayer) continue;

            for (var i = 0; i < code.Length; i++)
            {
                if (AudioApi.IsMatch(code[i])) offenders.Add($"{relativePath}:{i + 1}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Something outside AlertSoundPlayer reaches an audio API. One event is one sound: the "
            + "sound belongs to the balloon's show path, which runs once per notification, and NOT "
            + "to AlertDispatcher, whose fan-out loop runs once per destination. Route it through "
            + $"IAlertSoundPlayer instead.\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheSoundFenceSeesWhatItClaimsTo()
    {
        var files = AppSources();

        // Both clauses above pass green on an empty or mis-filtered walk.
        Assert.Contains(TheSoundPlayer, files.Select(f => f.RelativePath));
        Assert.Contains("src/ROROROblox.App/Discord/AlertDispatcher.cs", files.Select(f => f.RelativePath));

        // The pattern must still fire on the thing it is supposed to catch, or the absence it
        // reports is the regex's and not the app's.
        Assert.True(files.Single(f => f.RelativePath == TheSoundPlayer).Code
                .Any(l => AudioApi.IsMatch(l)),
            "AlertSoundPlayer no longer names an audio API, so either the player stopped playing "
            + "anything or the pattern broke — and NothingOutsideTheSoundPlayerTouchesAnAudioApi is "
            + "vacuous either way.");

        Assert.True(AudioApi.IsMatch("using var player = new SoundPlayer(stream);"));
        Assert.True(AudioApi.IsMatch("SystemSounds.Asterisk.Play();"));
        Assert.False(AudioApi.IsMatch("_sound.Play();"),
            "The audio pattern now matches the indirection itself, so every caller of "
            + "IAlertSoundPlayer would be reported as an offender.");

        // And the bracket walk has to actually walk. A span that ran off the end would make the
        // Contains clauses above assert against the rest of the file.
        Assert.Equal("(a { b })", BalancedSpan("x = f(a { b }); y", 5));
    }

    /// <summary>
    /// <paramref name="source"/> from <paramref name="from"/> through the delimiter that closes the
    /// construct opening there, brackets balanced — so "is this call inside the marshal" is a real
    /// question rather than a question about line order. Same helper
    /// <c>TrayWiringTests.DelimitedSpan</c> runs on, duplicated rather than shared because sharing
    /// it would mean one fence's refactor can quietly weaken another's.
    /// </summary>
    private static string BalancedSpan(string source, int from)
    {
        var depth = 0;
        var opened = false;
        for (var i = from; i < source.Length; i++)
        {
            if (source[i] is '(' or '{') { depth++; opened = true; }
            else if (source[i] is ')' or '}') { depth--; }
            if (opened && depth == 0) return source[from..(i + 1)];
        }

        Assert.Fail("Bracket scan ran off the end — the file does not parse the way this fence "
            + "assumes, so its verdict means nothing.");
        return "";
    }
}
