using System.IO;
using System.Text.RegularExpressions;
// Aliased, and NOT to the name `App`, for the reason RobloxLauncherTests records: inside
// namespace ROROROblox.Tests the compiler reads a bare `App.` as the sibling NAMESPACE
// ROROROblox.App, which wins over a using alias and fails with "SetMetricAlertsGate does not
// exist in the namespace ROROROblox.App". global:: on the right-hand side for the same reason.
using AppUnderTest = global::ROROROblox.App.App;

namespace ROROROblox.Tests;

/// <summary>
/// The metric-alert gate has two writers, and the slow one may not overwrite the fast one.
/// <para>
/// WHAT BROKE. Found by the final whole-branch review, 2026-09-11, in the seam between two tasks
/// that never touched the same file. <c>App</c> re-reads the opt-in on the view model's 30s tick;
/// the Settings toggle, new that day, writes the setting and then nudges the same cached bool.
/// Interleaved: the tick reads <c>true</c> and releases the settings semaphore, the user unticks
/// the box, that write persists <c>false</c> and nudges the gate to <c>false</c>, and then the
/// tick's continuation resumes and assigns its stale <c>true</c>. Metric breaches keep reaching
/// Discord and the phone for up to 30 seconds after an explicit opt-out. Before the toggle existed
/// the tick was the only writer and had nobody to overtake.
/// </para>
/// <para>
/// THE FIX, AND WHY IT IS SHAPED LIKE THIS. A generation counter bumped by the nudge, captured by
/// the tick before its read begins and checked as part of the assignment. Compare-then-assign
/// under one lock rather than two volatile steps, because a nudge landing between the compare and
/// the assignment would reintroduce the same defect in a narrower window — and a narrower window
/// onto the same 30 seconds of unwanted alerts is not a fix. The lock is never taken on the report
/// path (the gate closure reads the volatile bool with no lock at all) and never held across an
/// await: the settings read happens outside it, which is the reason the generation exists at all.
/// </para>
/// <para>
/// WHY THIS IS TESTABLE AND THE TOGGLE HANDLER IS NOT. The race lives in <c>App</c>, between two
/// plain static methods — no <c>Window</c>, no dispatcher, no UI seam. The Settings half is an
/// <c>async void</c> handler on a WPF page and stays covered by the manual smoke.
/// </para>
/// </summary>
public class MetricAlertsGateTests
{
    [Fact]
    public void ANudgeDuringTheRead_BeatsTheTicksStaleValue()
    {
        // The gate as it stands before the user touches anything: on, with a rules file present.
        AppUnderTest.SetMetricAlertsGate(true);

        // The tick's read begins. settings.json still says true at this instant.
        var generation = AppUnderTest.BeginMetricAlertsGateRead();

        // The user unticks the box. The write persists false and nudges the gate.
        AppUnderTest.SetMetricAlertsGate(false);

        // The tick resumes holding the value it read a moment ago. It must not land.
        Assert.False(AppUnderTest.TryCommitMetricAlertsGate(generation, enabled: true),
            "a read that started before the nudge is stale by definition and must be dropped.");
        Assert.False(AppUnderTest.MetricAlertsGateForTests,
            "the opt-out the user just made stands; this is the 30 seconds of alerts the fix exists to prevent.");
    }

    [Fact]
    public void WithNoNudge_TheTickCommitsNormally()
    {
        // The other half of the guarantee, and the one a too-eager fix would break: with nothing
        // racing it, the tick is still what picks up a hand-edited settings.json.
        AppUnderTest.SetMetricAlertsGate(false);
        var generation = AppUnderTest.BeginMetricAlertsGateRead();

        Assert.True(AppUnderTest.TryCommitMetricAlertsGate(generation, enabled: true));
        Assert.True(AppUnderTest.MetricAlertsGateForTests);

        AppUnderTest.SetMetricAlertsGate(false);
    }

    [Fact]
    public void ANudgeThatWritesTheSameValue_StillBlocksTheRead()
    {
        // The bump is unconditional on purpose. A nudge that writes what the gate already held
        // still means the user has spoken since the read began, and the read is no fresher for
        // having agreed with them.
        AppUnderTest.SetMetricAlertsGate(false);
        var generation = AppUnderTest.BeginMetricAlertsGateRead();
        AppUnderTest.SetMetricAlertsGate(false);

        Assert.False(AppUnderTest.TryCommitMetricAlertsGate(generation, enabled: true));
        Assert.False(AppUnderTest.MetricAlertsGateForTests);
    }

    /// <summary>
    /// The primitive above being correct and the tick USING it are different facts, and the second
    /// one is where this regresses: a future edit that assigns the cached bool directly would pass
    /// every test above it. So this reads the composition root and asserts the shape.
    /// </summary>
    [Fact]
    public void TheTick_CommitsThroughTheGenerationRatherThanAssigning()
    {
        var body = RefreshBody();

        Assert.Contains("BeginMetricAlertsGateRead()", body, StringComparison.Ordinal);
        Assert.Contains("TryCommitMetricAlertsGate(generation", body, StringComparison.Ordinal);
        Assert.DoesNotContain("MetricAlertsEnabled =", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the gate has exactly the two writers its lock covers. Counted over the whole file with
    /// comments stripped, so a third assignment anywhere in <c>App</c> — the place the first one
    /// appeared — fails here rather than at a user's desk.
    /// </summary>
    [Fact]
    public void TheCachedGate_IsAssignedInExactlyTwoPlaces()
    {
        var code = string.Join("\n", AppSource()
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        var assignments = Regex.Matches(code, @"\bMetricAlertsEnabled\s*=[^=]").Count;

        Assert.True(assignments == 2,
            $"App.xaml.cs assigns MetricAlertsEnabled {assignments} times; exactly two writers are "
            + "expected — SetMetricAlertsGate and TryCommitMetricAlertsGate, both under "
            + "MetricAlertsGateLock. A third one is a writer the generation counter does not see.");
    }

    /// <summary>The composition root's lines, found the same way every other source fence here
    /// finds the tree. No absolute path appears in this file.</summary>
    private static string[] AppSource()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "src", "ROROROblox.App", "App.xaml.cs");
        Assert.True(File.Exists(path), $"App.xaml.cs not found at {path}.");
        return File.ReadAllLines(path);
    }

    /// <summary>
    /// <c>RefreshMetricAlertsGateAsync</c>'s body, from its signature to the first line that is a
    /// closing brace at method indentation.
    /// </summary>
    private static string RefreshBody()
    {
        var lines = AppSource();
        var start = Array.FindIndex(lines, l => l.Contains(
            "private async Task RefreshMetricAlertsGateAsync(", StringComparison.Ordinal));
        Assert.True(start >= 0, "RefreshMetricAlertsGateAsync is gone or was renamed; this fence is blind.");

        var end = Array.FindIndex(lines, start, l => l.TrimEnd() == "    }");
        Assert.True(end > start, "couldn't find the end of RefreshMetricAlertsGateAsync.");

        return string.Join("\n", lines[start..(end + 1)]);
    }
}
