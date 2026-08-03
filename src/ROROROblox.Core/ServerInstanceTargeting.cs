namespace ROROROblox.Core;

/// <summary>
/// The single place a <see cref="LaunchTarget.GameJob"/> is ever constructed in production.
/// <para>
/// Deciding "should this launch go to one specific server, and which" is one rule, and it is easy
/// to get subtly wrong (see <see cref="ServerInstance"/> for the failure the 2026-08-02 spike hit).
/// Keeping it here means every consumer — Recycle, Squad Launch, and whatever comes next — inherits
/// the same answer, and the rule is testable without a client, a cookie, or a network.
/// </para>
/// </summary>
public static class ServerInstanceTargeting
{
    /// <summary>
    /// Given the target a launch would otherwise use and the account's current server (from
    /// presence, or <see langword="null"/> when unknown), return the target to actually launch.
    /// <list type="bullet">
    ///   <item><see cref="LaunchTarget.Place"/> + a known server -> that exact server. Both halves
    ///   come from <paramref name="server"/>; the incoming target's place id is discarded, because
    ///   it may name the universe's entry place rather than where the account actually is.</item>
    ///   <item>An existing <see cref="LaunchTarget.GameJob"/> + a known server -> the FRESH pair.
    ///   A remembered job id goes stale the moment Roblox matchmakes the account elsewhere.</item>
    ///   <item>An existing <see cref="LaunchTarget.GameJob"/> with NO known server -> degrade to
    ///   <see cref="LaunchTarget.Place"/>. An unverifiable job id may be dead, and a dead one is a
    ///   hard error that leaves the account at the home screen. Wrong server beats no game.</item>
    ///   <item><see cref="LaunchTarget.PrivateServer"/> -> untouched. Its code already identifies
    ///   exactly one server, durably; a job id is the perishable version of the same answer.</item>
    ///   <item>Everything else (<see cref="LaunchTarget.FollowFriend"/>,
    ///   <see cref="LaunchTarget.Home"/>, <see cref="LaunchTarget.DefaultGame"/>) -> untouched.</item>
    /// </list>
    /// </summary>
    public static LaunchTarget Upgrade(LaunchTarget current, ServerInstance? server)
    {
        ArgumentNullException.ThrowIfNull(current);

        return current switch
        {
            LaunchTarget.Place when server is not null => new LaunchTarget.GameJob(server.PlaceId, server.JobId),
            LaunchTarget.GameJob when server is not null => new LaunchTarget.GameJob(server.PlaceId, server.JobId),
            LaunchTarget.GameJob stale => new LaunchTarget.Place(stale.PlaceId),
            _ => current,
        };
    }
}
