namespace ROROROblox.Core;

/// <summary>
/// One launch event — a row in the session history. Created when an account begins launching;
/// stamped with <see cref="EndedAtUtc"/> when the tracker reports the player process exited.
/// "What did Pokey play yesterday" answered without scraping logs.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GameName"/> is whatever the row's selected game (or resolved private server)
/// said it was at launch time — captured at-launch so the history is a stable record even if
/// the game's name changes later in Roblox or the user removes the favorite.
/// </para>
/// <para>
/// <see cref="EndedAtUtc"/> stays null until <see cref="IRobloxProcessTracker"/> sees the
/// process exit. Sessions whose process never attaches (Roblox version drift, AV, etc.) get a
/// null end and a status hint via <see cref="OutcomeHint"/>.
/// </para>
/// </remarks>
public sealed record LaunchSession(
    Guid Id,
    Guid AccountId,
    string AccountDisplayName,
    string? AccountAvatarUrl,
    string? GameName,
    long? PlaceId,
    bool IsPrivateServer,
    DateTimeOffset LaunchedAtUtc,
    DateTimeOffset? EndedAtUtc,
    string? OutcomeHint)
{
    /// <summary>
    /// Convenience: how long the session ran. Null if the session is still in flight or never
    /// attached.
    /// </summary>
    public TimeSpan? Duration =>
        EndedAtUtc.HasValue ? EndedAtUtc.Value - LaunchedAtUtc : null;
}
