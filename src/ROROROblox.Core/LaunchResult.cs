namespace ROROROblox.Core;

/// <summary>
/// Why a launch failed, as a key the App can format (and Phase C can translate). Core never
/// composes the sentence — see the Core string boundary spec
/// (docs/superpowers/specs/2026-09-05-core-string-boundary-design.md) and
/// <c>CoreMessageCatalog</c> in the App, which owns the prose for each kind.
/// </summary>
public enum LaunchFailureKind
{
    /// <summary>Launch-default path with no default game configured anywhere.</summary>
    NoDefaultGame,

    /// <summary>The auth-ticket dance threw; <c>Detail</c> carries the exception text.</summary>
    AuthTicketFailed,

    /// <summary>No Roblox player installation was found at launch time.</summary>
    RobloxNotInstalled,

    /// <summary><c>Process.Start</c> threw; <c>Detail</c> carries the exception text.</summary>
    ProcessStartFailed,
}

/// <summary>
/// Outcome of <see cref="IRobloxLauncher.LaunchAsync"/>. Discriminated union — pattern match
/// to handle each case. <see cref="Started"/> carries the spawned process id;
/// <see cref="CookieExpired"/> means the row should flip to the yellow "Session expired" badge
/// (item 9 wires Re-authenticate); <see cref="Failed"/> carries a
/// <see cref="LaunchFailureKind"/> plus optional diagnostic detail — the user-facing sentence
/// is the App's to write (match on <c>Kind</c>, never on message text).
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
    public sealed record Limited : LaunchResult;

    /// <summary><paramref name="Detail"/> is diagnostic data (an exception's text), not a
    /// sentence — it lands after the catalog's headline, and may be logged verbatim.</summary>
    public sealed record Failed(LaunchFailureKind Kind, string? Detail = null) : LaunchResult;
}
