namespace ROROROblox.Core.Discord;

/// <summary>One account as presence sees it. <paramref name="DisplayName"/> is already rendered
/// through streamer mode by the caller — nothing downstream un-masks it.</summary>
public sealed record RosterAccount(
    Guid AccountId,
    string DisplayName,
    bool InGame,
    string? GameName,
    ServerInstance? Server,
    DateTimeOffset? InGameSinceUtc);

/// <summary>The whole roster at one instant. Presence describes the fleet, not one account.</summary>
public sealed record RosterSnapshot(IReadOnlyList<RosterAccount> Accounts);

/// <summary>What Discord should display. Null <see cref="JoinableServer"/> means no Join button.</summary>
public sealed record PresenceFields(
    string? Details,
    string? State,
    DateTimeOffset? StartedAtUtc,
    ServerInstance? JoinableServer);
