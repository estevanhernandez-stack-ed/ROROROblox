namespace ROROROblox.Core;

/// <summary>
/// Durable rollup of launch history. It exists because <see cref="SessionHistoryStore"/> caps at
/// <see cref="SessionHistoryStore.MaxRows"/> and prunes on every write — measured at roughly twenty
/// days of real use — so any lifetime number has to be accumulated as it happens rather than summed
/// from rows that are already gone. See spec §0.
/// </summary>
public interface ISessionStatsStore
{
    /// <summary>Fold one event into the rollup and persist.</summary>
    Task ApplyAsync(StatsEvent statsEvent);

    Task<SessionStats> ReadAsync();

    /// <summary>Drop everything. Called when history itself is cleared, so the two agree.</summary>
    Task ClearAsync();
}
