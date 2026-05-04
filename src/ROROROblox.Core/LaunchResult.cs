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

    public sealed record Started(int Pid) : LaunchResult;
    public sealed record CookieExpired : LaunchResult;
    public sealed record Failed(string Message) : LaunchResult;
}
