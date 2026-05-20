namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Polls each saved account's OWN presence (with its own cookie) against
/// <c>presence.roblox.com/v1/presence/users</c> and raises <see cref="AccountPresenceUpdated"/>
/// when a poll completes, mirroring the <see cref="IRobloxProcessTracker"/> event-source shape.
/// An account can always see its own presence, so self-querying returns server-truth state
/// untouched by privacy filters — this is what kills the "ghost" (a lost local pid can no longer
/// force a row to "Closed" once presence reports <see cref="UserPresenceType.InGame"/>). Spec §1.
/// </summary>
/// <remarks>
/// Item 1 (v1.5.0) covers the poll loop + presence→event mapping + game-name cache. The
/// resilience layer (401 expired-signal, 429 backoff, hold-last-on-failure) and the
/// <c>RequestImmediateRefreshAsync</c> fast-confirm hook are item 2 — this interface is shaped
/// so those slot in without rework.
/// </remarks>
public interface IPresenceService
{
    /// <summary>Fired once per polled target after each <see cref="PollOnceAsync"/> pass.</summary>
    event EventHandler<AccountPresenceEventArgs>? AccountPresenceUpdated;

    /// <summary>Start the internal <see cref="System.Threading.PeriodicTimer"/> poll loop.</summary>
    void Start();

    /// <summary>Stop the poll loop. Idempotent — safe to call when not started.</summary>
    void Stop();

    /// <summary>
    /// Run ONE poll pass over the current snapshot of targets (from the snapshot provider),
    /// querying each account's own presence and raising <see cref="AccountPresenceUpdated"/> for
    /// each. The timer loop calls this each tick; tests call it directly.
    /// </summary>
    Task PollOnceAsync(CancellationToken ct = default);
}

/// <summary>
/// One account the presence poller watches: the local saved-account id paired with its resolved
/// Roblox user id. Accounts without a resolved <see cref="RobloxUserId"/> aren't poll targets.
/// </summary>
public sealed record PresenceTarget(Guid AccountId, long RobloxUserId);

/// <summary>
/// Payload for <see cref="IPresenceService.AccountPresenceUpdated"/>. <see cref="GameName"/> is
/// non-null only when <see cref="PresenceType"/> is <see cref="UserPresenceType.InGame"/> AND the
/// game name resolved; otherwise null.
/// </summary>
public sealed class AccountPresenceEventArgs : EventArgs
{
    public AccountPresenceEventArgs(
        Guid accountId,
        UserPresenceType presenceType,
        long? placeId,
        string? gameName,
        DateTimeOffset occurredAtUtc)
    {
        AccountId = accountId;
        PresenceType = presenceType;
        PlaceId = placeId;
        GameName = gameName;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid AccountId { get; }
    public UserPresenceType PresenceType { get; }
    public long? PlaceId { get; }
    public string? GameName { get; }
    public DateTimeOffset OccurredAtUtc { get; }
}
