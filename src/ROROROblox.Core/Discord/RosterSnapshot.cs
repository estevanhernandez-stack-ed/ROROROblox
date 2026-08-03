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

/// <summary>What Discord should display. Null <see cref="JoinableServer"/> means no Join button.
/// <paramref name="JoinableServerAccountCount"/> is how many roster accounts are already in
/// <see cref="JoinableServer"/> — 0 when there is no joinable server. It is the correct Discord
/// party "Size": showing a party size smaller than the accounts actually together in that server
/// reads as self-contradicting next to the State line.</summary>
public sealed record PresenceFields(
    string? Details,
    string? State,
    DateTimeOffset? StartedAtUtc,
    ServerInstance? JoinableServer,
    int JoinableServerAccountCount);
