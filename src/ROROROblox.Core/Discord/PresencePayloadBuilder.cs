namespace ROROROblox.Core.Discord;

/// <summary>
/// Roster snapshot → Discord presence fields. Pure: no clock, no IPC, no I/O, so "what does
/// presence say when three of eight are in one server and five are elsewhere?" is a unit test
/// rather than something to check by eye in Discord.
/// </summary>
public static class PresencePayloadBuilder
{
    public static PresenceFields? Build(RosterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var live = snapshot.Accounts.Where(a => a.InGame).ToList();
        if (live.Count == 0)
        {
            return null;   // nothing running -> clear presence entirely
        }

        // The biggest cluster of accounts sharing one server is what a friend would want to join.
        var biggestCluster = live
            .Where(a => a.Server is not null)
            .GroupBy(a => a.Server!.JobId, StringComparer.OrdinalIgnoreCase)
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

        return new PresenceFields(
            Details: details,
            State: state,
            StartedAtUtc: live.Where(a => a.InGameSinceUtc is not null).Min(a => a.InGameSinceUtc),
            JoinableServer: biggestCluster?.First().Server,
            JoinableServerAccountCount: togetherCount);
    }
}
