namespace ROROROblox.Core;

/// <summary>
/// Seeds the rollup from whatever history rows still exist, once (spec §4).
///
/// <para>Without this the stats page reads zero on the day it ships, which is a poor first
/// impression of a feature whose entire appeal is an accumulated number. With it, an existing user
/// starts with however much of the rolling window survives — roughly twenty days of real use.</para>
///
/// <para>Explicitly best-effort. It recovers uptime, per-account, per-game, per-day, and streaks.
/// It CANNOT recover peak concurrent alts: that is a property of a moment rather than of any
/// session, and the rows for the third and fourth simultaneous alt may already have been pruned.
/// The UI labels that record accordingly rather than implying it is all-time.</para>
/// </summary>
public static class SessionStatsBackfill
{
    public static async Task RunOnceAsync(ISessionHistoryStore history, ISessionStatsStore stats)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(stats);

        // The latch is read from the rollup itself rather than a side file, so it cannot drift
        // away from the data it describes.
        if ((await stats.ReadAsync().ConfigureAwait(false)).Backfilled) return;

        var rows = await history.ListAsync().ConfigureAwait(false);

        // Oldest first: streaks are built by walking days forward, so replaying newest-first would
        // record a chain that never happened.
        foreach (var row in rows.OrderBy(r => r.LaunchedAtUtc))
        {
            await stats.ApplyAsync(new StatsEvent.LaunchRecorded(
                row.AccountId, row.PlaceId, row.GameName, row.LaunchedAtUtc)).ConfigureAwait(false);

            if (row.EndedAtUtc is { } ended)
            {
                await stats.ApplyAsync(new StatsEvent.SessionEnded(
                    row.AccountId, row.PlaceId, row.LaunchedAtUtc, ended)).ConfigureAwait(false);
            }
            else
            {
                // Still in flight, or its client died without recording an end. Either way the
                // duration is unknowable, so count it rather than invent one (§5).
                await stats.ApplyAsync(new StatsEvent.SessionMissingEnd()).ConfigureAwait(false);
            }
        }

        // Latched even when there was nothing to read, so a user with no history does not re-run
        // the scan on every startup forever.
        await stats.ApplyAsync(new StatsEvent.BackfillCompleted()).ConfigureAwait(false);
    }
}
