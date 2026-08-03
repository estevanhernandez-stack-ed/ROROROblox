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

/// <summary>The whole roster at one instant. Presence describes the fleet, not one account.</summary>
public sealed record RosterSnapshot(IReadOnlyList<RosterAccount> Accounts);

/// <summary>
/// One running server as presence + Join sees it — the (place, job) pair a public Join secret
/// targets, plus the private-server credential when the session that landed there was actually
/// launched into a private server.
/// <para>
/// Never constructed by hand: <see cref="TryFrom"/> is the only entrance, and it refuses to pair a
/// private-server code with a place id that doesn't match the code's own place — the same
/// matched-pair discipline <see cref="ServerInstance"/> already enforces for (place, job). A stale
/// <see cref="LaunchTarget.PrivateServer"/> left over from an account that has since teleported to
/// a different place (or was recycled into a plain <see cref="LaunchTarget.GameJob"/>) can never
/// get its old code attached to a server it does not name.
/// </para>
/// </summary>
public sealed record RosterServer
{
    public ServerInstance Server { get; }
    public string? PrivateServerCode { get; }
    public PrivateServerCodeKind? PrivateServerCodeKind { get; }

    private RosterServer(ServerInstance server, string? privateServerCode, PrivateServerCodeKind? privateServerCodeKind)
    {
        Server = server;
        PrivateServerCode = privateServerCode;
        PrivateServerCodeKind = privateServerCodeKind;
    }

    /// <summary>
    /// Build from a presence server pairing plus the <see cref="LaunchTarget"/> the session
    /// actually launched with (<c>AccountSummary.LastLaunchTarget</c>). The private-server code is
    /// attached ONLY when that launch target is a <see cref="LaunchTarget.PrivateServer"/> whose
    /// <c>PlaceId</c> matches <paramref name="server"/>'s — anything else (a public launch, a
    /// follow-join, or a private-server target for a DIFFERENT place than the one presence reports
    /// right now) yields a public-only <see cref="RosterServer"/> rather than a mismatched pairing.
    /// </summary>
    public static RosterServer? TryFrom(ServerInstance? server, LaunchTarget? lastLaunchTarget)
    {
        if (server is null)
        {
            return null;
        }

        if (lastLaunchTarget is LaunchTarget.PrivateServer ps && ps.PlaceId == server.PlaceId)
        {
            return new RosterServer(server, ps.Code, ps.Kind);
        }

        return new RosterServer(server, null, null);
    }
}

/// <summary>What Discord should display. Null <see cref="JoinableServer"/> means no Join button.
/// <paramref name="JoinableServerAccountCount"/> is how many roster accounts are already in
/// <see cref="JoinableServer"/> — 0 when there is no joinable server. It is the correct Discord
/// party "Size": showing a party size smaller than the accounts actually together in that server
/// reads as self-contradicting next to the State line.</summary>
public sealed record PresenceFields(
    string? Details,
    string? State,
    DateTimeOffset? StartedAtUtc,
    RosterServer? JoinableServer,
    int JoinableServerAccountCount);
