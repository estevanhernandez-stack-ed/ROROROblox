namespace ROROROblox.Core.Discord;

/// <summary>One account as presence sees it. <paramref name="DisplayName"/> is already rendered
/// through streamer mode by the caller — nothing downstream un-masks it.</summary>
public sealed record RosterAccount(
    Guid AccountId,
    string DisplayName,
    bool InGame,
    string? GameName,
    RosterServer? Server,
    DateTimeOffset? InGameSinceUtc);

/// <summary>The whole roster at one instant. Presence describes the fleet, not one account.
/// <para>
/// <paramref name="IsStreamerModeActive"/> (2026-08-03) is the SAME streamer-identity signal that
/// already drives <c>RosterAccount.DisplayName</c> via <c>AccountSummary.RenderName</c> — carried
/// alongside the accounts so <see cref="PresencePayloadBuilder"/> can decide whether the roster
/// COUNT (not just the names) is safe to publish, without reaching for a streamer-mode singleton
/// itself. Names were already masked outbound; the count is the same category of disclosure
/// ("3 of 8" tells a viewer exactly how big the fleet is) and had been leaking through the party
/// numbers and the state text right alongside the (already-masked) names. Default <see langword="false"/>
/// so every positional <c>new RosterSnapshot([...])</c> call site written before this field existed
/// keeps compiling and keeps its old (non-anonymized) behavior.
/// </para>
/// </summary>
public sealed record RosterSnapshot(IReadOnlyList<RosterAccount> Accounts, bool IsStreamerModeActive = false);

/// <summary>
/// One running server as presence + Join sees it — the (place, job) pair presence reports right
/// now, plus the private-server credential when the session that landed there was actually
/// launched into a private server.
/// <para>
/// Re-review, 2026-08-03 (blocking finding): the original version of this type paired the private
/// code with <paramref name="server"/> only when <c>ps.PlaceId == server.PlaceId</c> — treating
/// place-id equality as a proxy for "is this presence reading still the private server we
/// launched." That proxy is false for every Pet Simulator 99 private-server session: Pet Sim
/// teleports players between places INSIDE one universe (see <see cref="ServerInstance"/>'s doc
/// comment), so <c>LastLaunchTarget</c> keeps recording the ENTRY place while presence reports the
/// TELEPORTED place. The equality check dropped the private code silently, every time, for exactly
/// the audience this feature exists for.
/// </para>
/// <para>
/// The corrected rule: <paramref name="lastLaunchTarget"/> being a <see cref="LaunchTarget.PrivateServer"/>
/// is presence-independent identity — the place id and code that address the private server come
/// from that SAME <see cref="LaunchTarget.PrivateServer"/> value (they travel together by
/// construction, the same matched-pair discipline <see cref="ServerInstance"/> enforces for
/// (place, job) — just enforced by "one record, two fields" instead of an equality check).
/// <paramref name="server"/> (presence) is consulted only for LIVENESS — is this account in a game
/// at all — and for clustering ("which cluster is it part of," via <see cref="ServerInstance.JobId"/>
/// in <c>PresencePayloadBuilder</c>). Presence is never asked to agree about WHICH place the
/// account is in before the private code is trusted.
/// </para>
/// <para>
/// A stale credential is still guarded against, just not by place matching: <c>MainViewModel.ApplyPresence</c>
/// clears <c>AccountSummary.LastLaunchTarget</c> the moment presence reports the account fully out
/// of a game (Minor 1, re-review 2026-08-03) — an account cannot join a genuinely different server
/// (public or private) without first leaving whatever it was in, so that transition is the
/// deterministic point to drop an old private-server credential. A within-session universe
/// teleport (the blocking-finding case) never passes through "not in game," so it never triggers
/// that clear.
/// </para>
/// </summary>
public sealed record RosterServer
{
    public ServerInstance Server { get; }

    /// <summary>
    /// The private server's OWN place id — from the <see cref="LaunchTarget.PrivateServer"/> that
    /// was actually launched, never from <see cref="Server"/> (presence). Building the Join secret
    /// from presence's place would send a real private-server code to whatever place the account
    /// happens to be teleported into right now, which is exactly the address Roblox rejects with
    /// "This experience has ended, or the server became unavailable unexpectedly."
    /// </summary>
    public long? PrivateServerPlaceId { get; }

    public string? PrivateServerCode { get; }
    public PrivateServerCodeKind? PrivateServerCodeKind { get; }

    private RosterServer(ServerInstance server, long? privateServerPlaceId, string? privateServerCode, PrivateServerCodeKind? privateServerCodeKind)
    {
        Server = server;
        PrivateServerPlaceId = privateServerPlaceId;
        PrivateServerCode = privateServerCode;
        PrivateServerCodeKind = privateServerCodeKind;
    }

    /// <summary>
    /// Build from a presence server pairing (liveness + clustering) plus the <see cref="LaunchTarget"/>
    /// the session actually launched with (<c>AccountSummary.LastLaunchTarget</c>). The private
    /// server's place id + code + kind are attached whenever that launch target is a
    /// <see cref="LaunchTarget.PrivateServer"/> — presence's own place id is never consulted for
    /// this decision. Returns <see langword="null"/> only when <paramref name="server"/> itself is
    /// null (offline, privacy-hidden, or before the first poll) — no server means nothing to join.
    /// </summary>
    public static RosterServer? TryFrom(ServerInstance? server, LaunchTarget? lastLaunchTarget)
    {
        if (server is null)
        {
            return null;
        }

        if (lastLaunchTarget is LaunchTarget.PrivateServer ps)
        {
            return new RosterServer(server, ps.PlaceId, ps.Code, ps.Kind);
        }

        return new RosterServer(server, null, null, null);
    }
}

/// <summary>What Discord should display. Null <see cref="JoinableServer"/> means no Join button.
/// <paramref name="JoinableServerAccountCount"/> is how many roster accounts are already in
/// <see cref="JoinableServer"/> — 0 when there is no joinable server. It is the correct Discord
/// party "Size": showing a party size smaller than the accounts actually together in that server
/// reads as self-contradicting next to the State line.
/// <para>
/// <paramref name="JoinableServerAccountMax"/> (2026-08-03) is the Discord party "Max" — the honest
/// ceiling is the user's TOTAL saved-account count (from the roster snapshot, live or not), never
/// an arbitrary constant: "3 of 8" reads as three of my eight accounts, and 8 is the only number
/// that is actually true. See <see cref="PresencePayloadBuilder"/>'s remarks for the full-roster
/// edge case (size == max) and streamer mode's neutral-placeholder override.
/// </para>
/// <para>
/// <paramref name="IsIdle"/> replaces the old "null means nothing running, so clear presence"
/// signalling (2026-08-03, live smoke test). The RPC connection stays open regardless of what is
/// running, so a cleared entry still renders in Discord — just as a bare "Playing RoRoRo" with no
/// artwork and no text, which reads as broken rather than absent. An idle payload is deliberate
/// instead: honest text, the <c>idle_large</c> artwork, and never a Join target — an idle entry is
/// not joinable and has no elapsed run, so <see cref="JoinableServer"/> and
/// <see cref="StartedAtUtc"/> are always null/absent when <paramref name="IsIdle"/> is true. The
/// caller (<c>DiscordPresenceService.Refresh</c>) uses the flag only to choose which large-image
/// key to send; it must not re-derive idle-ness or invent its own idle text — that decision lives
/// here, where it is a table of cases a unit test can pin down.
/// </para>
/// </summary>
public sealed record PresenceFields(
    string? Details,
    string? State,
    DateTimeOffset? StartedAtUtc,
    RosterServer? JoinableServer,
    int JoinableServerAccountCount,
    int JoinableServerAccountMax,
    bool IsIdle = false);
