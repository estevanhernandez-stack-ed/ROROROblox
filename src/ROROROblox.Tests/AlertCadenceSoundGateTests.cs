using System.IO;
using System.Text.RegularExpressions;
using ROROROblox.Core.Discord;
// Aliased, and NOT to the name `App`, for the reason MetricAlertsGateTests records: inside
// namespace ROROROblox.Tests the compiler reads a bare `App.` as the sibling NAMESPACE
// ROROROblox.App, which wins over a using alias. global:: on the right-hand side for the same
// reason.
using AppUnderTest = global::ROROROblox.App.App;

namespace ROROROblox.Tests;

/// <summary>
/// The cadence and sound caches each gained a second writer in v1.33 item 6, and the slow one may
/// not overwrite the fast one.
/// <para>
/// WHAT WOULD HAVE BROKEN. Items 2 and 5 shipped <c>App.AlertCadenceSetting</c> and
/// <c>App.AlertSoundSetting</c> as plain <c>volatile</c> fields refreshed on the view model's 30s
/// tick, and both said so in their own doc comments: one writer, no lock, correct only while the
/// tick is alone. Item 6 adds the controls that edit them, and a control that saves without
/// nudging the cache leaves the user waiting up to 30 seconds for a pace or a sound they just
/// chose. Nudging without a generation counter is worse than waiting: it is exactly the
/// interleaving <see cref="MetricAlertsGateTests"/> documents — the tick reads, the user changes
/// their mind, the nudge lands, and then the tick's continuation commits the value from before the
/// change.
/// </para>
/// <para>
/// WHY BOTH AND NOT JUST THE CADENCE. The checklist owed the counter to the cadence and said the
/// sound was "the same shape with the same single writer, so if the page nudges one it must nudge
/// both". The page nudges both, so both are here. They are separate counters rather than one
/// shared bump: a cadence change must not invalidate an in-flight sound read, which would turn
/// every save into a dropped read of the other setting.
/// </para>
/// <para>
/// WHAT IS NOT HERE. The Settings handlers themselves are <c>async void</c> methods on a WPF page
/// and stay covered by the manual smoke, the same split MetricAlertsGateTests draws. What IS
/// assertable is that the page's save path calls the nudge at all, which the source fence at the
/// bottom of this file reads out of SettingsPage.xaml.cs.
/// </para>
/// </summary>
public class AlertCadenceSoundGateTests
{
    // ---- cadence ----------------------------------------------------------------------------

    [Fact]
    public void ACadenceNudgeDuringTheRead_BeatsTheTicksStaleValue()
    {
        var chosen = AlertCadence.FromSettings(1, null);
        var stale = AlertCadence.FromSettings(60, null);

        AppUnderTest.SetAlertCadence(stale);

        // The tick's read begins. settings.json still says 60 at this instant.
        var generation = AppUnderTest.BeginAlertCadenceRead();

        // The user picks "at most once every minute". The write persists 1 and nudges the cache.
        AppUnderTest.SetAlertCadence(chosen);

        Assert.False(AppUnderTest.TryCommitAlertCadence(generation, stale),
            "a read that started before the nudge is stale by definition and must be dropped.");
        Assert.Equal(TimeSpan.FromMinutes(1), AppUnderTest.AlertCadenceForTests.Global);
    }

    [Fact]
    public void WithNoNudge_TheCadenceTickCommitsNormally()
    {
        // The half a too-eager fix breaks: with nothing racing it, the tick is still what picks up
        // a hand-edited settings.json.
        AppUnderTest.SetAlertCadence(AlertCadence.Default);
        var generation = AppUnderTest.BeginAlertCadenceRead();

        var wanted = AlertCadence.FromSettings(30, null);
        Assert.True(AppUnderTest.TryCommitAlertCadence(generation, wanted));
        Assert.Equal(TimeSpan.FromMinutes(30), AppUnderTest.AlertCadenceForTests.Global);

        AppUnderTest.SetAlertCadence(AlertCadence.Default);
    }

    [Fact]
    public void ACadenceNudgeThatWritesTheSameValue_StillBlocksTheRead()
    {
        // The bump is unconditional on purpose. A nudge that writes what the cache already held
        // still means the user has spoken since the read began.
        AppUnderTest.SetAlertCadence(AlertCadence.Default);
        var generation = AppUnderTest.BeginAlertCadenceRead();
        AppUnderTest.SetAlertCadence(AlertCadence.Default);

        Assert.False(AppUnderTest.TryCommitAlertCadence(generation, AlertCadence.FromSettings(0, null)));
        Assert.Equal(AlertCadence.DefaultQuietPeriod, AppUnderTest.AlertCadenceForTests.Global);
    }

    // ---- sound ------------------------------------------------------------------------------

    [Fact]
    public void ASoundNudgeDuringTheRead_BeatsTheTicksStaleValue()
    {
        AppUnderTest.SetAlertSound(AlertSound.Chime);

        var generation = AppUnderTest.BeginAlertSoundRead();

        // The user picks Silent. This is the direction that matters: a stale commit here puts a
        // noise back after an explicit request for quiet.
        AppUnderTest.SetAlertSound(AlertSound.Silent);

        Assert.False(AppUnderTest.TryCommitAlertSound(generation, AlertSound.Chime));
        Assert.Equal(AlertSound.Silent, AppUnderTest.AlertSoundForTests);

        AppUnderTest.SetAlertSound(AlertSoundSetting.Default);
    }

    [Fact]
    public void WithNoNudge_TheSoundTickCommitsNormally()
    {
        AppUnderTest.SetAlertSound(AlertSound.Chime);
        var generation = AppUnderTest.BeginAlertSoundRead();

        Assert.True(AppUnderTest.TryCommitAlertSound(generation, AlertSound.WindowsDefault));
        Assert.Equal(AlertSound.WindowsDefault, AppUnderTest.AlertSoundForTests);

        AppUnderTest.SetAlertSound(AlertSoundSetting.Default);
    }

    [Fact]
    public void TheTwoCountersAreIndependent()
    {
        // One shared generation would make every cadence save drop an in-flight sound read, and
        // vice versa — a fix that manufactures the staleness it exists to prevent.
        AppUnderTest.SetAlertSound(AlertSound.Chime);
        var soundGeneration = AppUnderTest.BeginAlertSoundRead();

        AppUnderTest.SetAlertCadence(AlertCadence.FromSettings(15, null));

        Assert.True(AppUnderTest.TryCommitAlertSound(soundGeneration, AlertSound.WindowsDefault),
            "a cadence nudge said nothing about the sound and must not invalidate its read.");

        AppUnderTest.SetAlertSound(AlertSoundSetting.Default);
        AppUnderTest.SetAlertCadence(AlertCadence.Default);
    }

    // ---- the refreshes and the page actually use them ---------------------------------------

    /// <summary>
    /// The primitives being correct and the ticks USING them are different facts, and the second
    /// is where this regresses: an edit that assigns the cached field directly passes every test
    /// above. Same shape as <c>MetricAlertsGateTests.TheTick_CommitsThroughTheGeneration…</c>.
    /// </summary>
    [Theory]
    [InlineData("RefreshAlertCadenceAsync", "BeginAlertCadenceRead()", "TryCommitAlertCadence(generation", "AlertCadenceSetting =")]
    [InlineData("RefreshAlertSoundAsync", "BeginAlertSoundRead()", "TryCommitAlertSound(generation", "AlertSoundSetting =")]
    public void TheTick_CommitsThroughTheGenerationRatherThanAssigning(
        string method, string begin, string commit, string directAssignment)
    {
        var body = MethodBody($"private async Task {method}(");

        Assert.Contains(begin, body, StringComparison.Ordinal);
        Assert.Contains(commit, body, StringComparison.Ordinal);
        Assert.DoesNotContain(directAssignment, body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each cache has exactly the two writers its lock covers. Counted over the whole file with
    /// comments stripped, so a third assignment anywhere in <c>App</c> fails here rather than at a
    /// user's desk. The field initialisers are excluded by the regex requiring no preceding
    /// <c>=</c> and by stripping declaration lines, which is why the expected count is two and not
    /// three.
    /// </summary>
    [Theory]
    [InlineData("AlertCadenceSetting")]
    [InlineData("AlertSoundSetting")]
    public void TheCachedValue_IsAssignedInExactlyTwoPlaces(string field)
    {
        var code = string.Join("\n", AppSource()
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            // The declaration line carries an initialiser that is not a writer. Dropping it by
            // the `volatile` keyword rather than by line number so a reformat cannot blind this.
            .Where(line => !line.Contains("private static volatile", StringComparison.Ordinal)));

        var assignments = Regex.Matches(code, @"\b" + Regex.Escape(field) + @"\s*=[^=]").Count;

        Assert.True(assignments == 2,
            $"App.xaml.cs assigns {field} {assignments} times; exactly two writers are expected — "
            + "the setter and the try-commit, both under the same lock. A third one is a writer the "
            + "generation counter does not see.");
    }

    /// <summary>
    /// And the Settings page nudges both, which is the whole reason the counters exist. A save that
    /// persists the setting and skips the nudge is the defect items 2 and 5 left open; a nudge on
    /// one setting and not the other is the half-fix the checklist specifically warned about.
    /// </summary>
    [Theory]
    [InlineData("App.SetAlertCadence(")]
    [InlineData("App.SetAlertSound(")]
    public void TheSettingsPage_NudgesTheRunningCache(string call)
    {
        Assert.Contains(call, SettingsPageSource(), StringComparison.Ordinal);
    }

    private static string[] AppSource()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "src", "ROROROblox.App", "App.xaml.cs");
        Assert.True(File.Exists(path), $"App.xaml.cs not found at {path}.");
        return File.ReadAllLines(path);
    }

    private static string SettingsPageSource()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "src", "ROROROblox.App", "Preferences", "SettingsPage.xaml.cs");
        Assert.True(File.Exists(path), $"SettingsPage.xaml.cs not found at {path}.");
        return File.ReadAllText(path);
    }

    private static string MethodBody(string signature)
    {
        var lines = AppSource();
        var start = Array.FindIndex(lines, l => l.Contains(signature, StringComparison.Ordinal));
        Assert.True(start >= 0, $"{signature} is gone or was renamed; this fence is blind.");

        var end = Array.FindIndex(lines, start, l => l.TrimEnd() == "    }");
        Assert.True(end > start, $"couldn't find the end of {signature}.");

        return string.Join("\n", lines[start..(end + 1)]);
    }
}
