namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Names the one process shape that is safe to end before a launch: an idle Roblox tray client.
///
/// <para>Why (#241, live finding 2026-10-26): a closing game spawns a RobloxPlayerBeta child with
/// <c>--launch-to-tray</c> and no window, and that idle client takes over the next roblox-player
/// launch. RoRoRo's tracked launcher then exits after about 4 s while the game runs in an untracked
/// process.</para>
///
/// <para>Why the command line and not age or windowless-only: a hand-started browser launch is
/// windowless and untracked while it loads, and the tray child a stop-then-relaunch meets is one
/// second old, so neither signal separates a tray client from a game still starting. Only
/// <c>--launch-to-tray</c> does.</para>
///
/// <para><b>Fails closed.</b> A null command line (unreadable process) is never a candidate.</para>
/// </summary>
public static class TrayClient
{
    public const string Flag = "--launch-to-tray";

    /// <summary>
    /// <paramref name="minAge"/> is the optional age floor (#222 sweeper). Null keeps the original
    /// rule exactly. With a floor, a client younger than it is never idle (a client still creating
    /// its window is young by any measure), and an unknown start time fails closed. A start time in
    /// the future (clock skew) reads as age zero, so it is also never swept.
    /// <paramref name="now"/> is a test seam; it defaults to the wall clock.
    /// </summary>
    public static bool IsIdleTrayClient(RobloxProcessInfo p, Func<int, bool> isTracked, TimeSpan? minAge = null, DateTimeOffset? now = null)
        => !p.HasWindow
           && p.CommandLine is { } cmd
           && cmd.Contains(Flag, StringComparison.OrdinalIgnoreCase)
           && !isTracked(p.Pid)
           && (minAge is not { } floor || (p.StartedAt is { } started && (now ?? DateTimeOffset.UtcNow) - started >= floor));
}
