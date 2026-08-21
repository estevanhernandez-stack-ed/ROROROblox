using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// A <see cref="FactAttribute"/> for tests that render a whole app <c>Window</c> through
/// <see cref="WindowRenderHost"/>. Identical to <c>[Fact]</c> everywhere except CI, where it skips
/// with a reason instead of timing out.
///
/// <para><b>Why this exists.</b> The window-render harness landed 2026-08-12 and was never run on a
/// CI runner before it merged. On one, a single render wedges: <c>AboutWindow mark [flatline]
/// @96dpi</c> "began rendering after 0.0s of queue wait and then did not finish" against the 60s
/// budget. Locally the same render takes milliseconds. Different tests drew the short straw on each
/// run — AboutMark, HistoryRow, BannerPair — because whichever one goes first hits the wall.</para>
///
/// <para><b>This is a coverage reduction and it is meant to be visible.</b> Nine gates do not run on
/// CI. They are SKIPPED, not passed: the run reports <c>Skipped: 9</c> and each carries the reason
/// below. That distinction is the whole point — F-105 is a row about a suite that printed
/// <c>Failed: 0</c> on a run that had stopped early, and swapping one silent-green for another
/// would be repeating it with extra steps.</para>
///
/// <para><b>The gates still run, on every developer machine and before every release.</b> They are
/// not disabled; they are environment-gated, and the environment they need is a desktop session.
/// Whoever fixes the harness for headless CI deletes this file and gets the nine back.</para>
/// </summary>
public sealed class WindowRenderFactAttribute : FactAttribute
{
    public WindowRenderFactAttribute()
    {
        if (WindowRenderAvailability.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>
/// The <see cref="TheoryAttribute"/> twin of <see cref="WindowRenderFactAttribute"/>, for a gate
/// that runs the same measurement over several windows. Same environment rule, same reason string —
/// declared here rather than in the test file so there is one place that knows when whole-window
/// rendering is available, and deleting this file still gets everything back at once.
/// </summary>
public sealed class WindowRenderTheoryAttribute : TheoryAttribute
{
    public WindowRenderTheoryAttribute()
    {
        if (WindowRenderAvailability.SkipReason is { } reason)
        {
            Skip = reason;
        }
    }
}

/// <summary>
/// Decides whether whole-window rendering is available in the current environment. Separate from
/// the attribute so the reason string has one home and can be asserted against.
/// </summary>
internal static class WindowRenderAvailability
{
    /// <summary>
    /// Non-null when these tests should be skipped, and the value is the reason the runner prints.
    /// Null on a normal desktop, which is every developer machine.
    /// </summary>
    /// <summary>
    /// Always null since 2026-08-21: these gates run everywhere, including CI.
    /// <para>
    /// They were skipped on CI because "a single render exceeds the 60s budget having started
    /// immediately", and that turned out to be F-105 wearing a second disguise. The harness was
    /// running the app's REAL startup inside the test host — plugin registry scan, plugin process
    /// launch, and an update check over the network — which will burn sixty seconds on a runner
    /// without the render being slow at all. With startup suppressed, CI returns
    /// <c>Skipped: 0, Total: 1798</c> on both x64 and arm64.
    /// </para>
    /// <para>
    /// The property is KEPT rather than the attributes rewritten: nine call sites say
    /// <c>[WindowRenderFact]</c>, and this is the one place a future environment-specific skip would
    /// belong. <c>RORORO_FORCE_WINDOW_RENDER</c> is gone with the branch it overrode — an escape
    /// hatch from a door that is no longer locked.
    /// </para>
    /// </summary>
    public static string? SkipReason => null;

}
