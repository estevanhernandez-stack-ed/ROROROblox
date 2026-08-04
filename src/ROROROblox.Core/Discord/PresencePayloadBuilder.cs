namespace ROROROblox.Core.Discord;

/// <summary>
/// Roster snapshot → Discord presence fields. Pure: no clock, no IPC, no I/O, so "what does
/// presence say when three of eight are in one server and five are elsewhere?" is a unit test
/// rather than something to check by eye in Discord.
/// </summary>
public static class PresencePayloadBuilder
{
    public static PresenceFields Build(RosterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var live = snapshot.Accounts.Where(a => a.InGame).ToList();
        if (live.Count == 0)
        {
            return BuildIdle(snapshot);
        }

        // The biggest cluster of accounts sharing one server is what a friend would want to join.
        var biggestCluster = live
            .Where(a => a.Server is not null)
            .GroupBy(a => a.Server!.Server.JobId, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        var togetherCount = biggestCluster?.Count() ?? 0;
        var state = live.Count == 1
            ? "1 account"
            : togetherCount == live.Count
                ? $"{live.Count} accounts in one server"
                : togetherCount > 1
                    ? $"{live.Count} accounts · {togetherCount} in this server"
                    : $"{live.Count} accounts";

        // Details should reflect the game the Join button points at (biggestCluster), not the roster-first account.
        var details = biggestCluster?.Select(a => a.GameName)
                          .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                      ?? live.Select(a => a.GameName)
                          .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

        // The cluster's private/public nature is decided from the SAME accounts the cluster is
        // built from, not an unrelated row: every member shares the same (place, job) pair by
        // construction (grouped by JobId), but only the member(s) whose LastLaunchTarget was
        // actually a PrivateServer carry the code — others in the same physical server may have
        // arrived via Follow, a plain GameJob, or a since-cleared LastLaunchTarget (MainViewModel
        // clears it once an account fully leaves a game — see RosterServer's remarks). Preferring a
        // member that carries the code (when one exists) keeps a private session correctly
        // identified even when .First() would otherwise land on a member that never recorded one.
        var joinableRepresentative = biggestCluster?.FirstOrDefault(a => a.Server?.PrivateServerCode is not null)
                                      ?? biggestCluster?.First();

        return new PresenceFields(
            Details: details,
            State: state,
            StartedAtUtc: live.Where(a => a.InGameSinceUtc is not null).Min(a => a.InGameSinceUtc),
            JoinableServer: joinableRepresentative?.Server,
            JoinableServerAccountCount: togetherCount);
    }

    /// <summary>
    /// Nothing running is not "nothing to say." The RPC connection stays open regardless (see
    /// <see cref="PresenceFields"/>'s remarks), so the choice is between a blank-looking entry and
    /// a deliberate one. The only thing the roster actually knows in this state is how many saved
    /// accounts are standing by — that is real information, so it is what the idle entry says.
    /// Never a game name, never elapsed time, never a Join target: none of those are true right now.
    /// </summary>
    private static PresenceFields BuildIdle(RosterSnapshot snapshot)
    {
        var saved = snapshot.Accounts.Count;
        var details = saved switch
        {
            0 => "No saved accounts yet",
            1 => "1 saved account, standing by",
            _ => $"{saved} saved accounts, standing by",
        };

        return new PresenceFields(
            Details: details,
            State: "RoRoRo — multi-instance for Roblox",
            StartedAtUtc: null,
            JoinableServer: null,
            JoinableServerAccountCount: 0,
            IsIdle: true);
    }
}
