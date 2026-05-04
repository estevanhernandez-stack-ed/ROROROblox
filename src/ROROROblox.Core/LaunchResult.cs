namespace ROROROblox.Core;

/// <summary>
/// Outcome of <see cref="IRobloxLauncher.LaunchAsync"/>. Discriminated union — pattern match
/// to handle each case. <see cref="Started"/> carries the spawned process id;
/// <see cref="CookieExpired"/> means the row should flip to the yellow "Session expired" badge
/// (item 9 wires Re-authenticate); <see cref="Failed"/> carries a user-facing message
/// (Roblox-not-installed, missing default place, etc.).
/// </summary>
public abstract record LaunchResult
{
    private LaunchResult() { }

    /// <summary>
    /// <paramref name="Pid"/> is the launcher process id (<c>RobloxPlayerLauncher.exe</c>),
    /// which exits within seconds after spawning <c>RobloxPlayerBeta.exe</c>.
    /// <paramref name="LaunchedAtUtc"/> is the moment we handed the URI to the shell — used by
    /// <c>IRobloxProcessTracker</c> to match the resulting player process by start-time.
    /// </summary>
    public sealed record Started(int Pid, DateTimeOffset LaunchedAtUtc) : LaunchResult;
    public sealed record CookieExpired : LaunchResult;
    public sealed record Failed(string Message) : LaunchResult;
}
