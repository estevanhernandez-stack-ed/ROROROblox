namespace ROROROblox.Core;

/// <summary>
/// Persisted log of recent launches across all accounts. Bounded — only the most recent 100
/// rows are kept on disk; older entries fall off as new ones come in. Plaintext (no secrets,
/// just public-ish metadata: account display name, game name, timestamps).
/// </summary>
public interface ISessionHistoryStore
{
    Task<IReadOnlyList<LaunchSession>> ListAsync();

    /// <summary>Append a new in-flight session (no end timestamp yet).</summary>
    Task AddAsync(LaunchSession session);

    /// <summary>
    /// Mark a session ended. Looks up by id; no-op if the row's been pruned. Call this only when the
    /// session actually ran and is now over: it is the one path that gives a row a duration, and the
    /// stats rollup folds every ended row into uptime.
    /// </summary>
    Task MarkEndedAsync(Guid sessionId, DateTimeOffset endedAtUtc, string? outcomeHint = null);

    /// <summary>
    /// Record how a launch turned out WITHOUT stamping an end: the row keeps a null
    /// <see cref="LaunchSession.EndedAtUtc"/> (so no duration, and nothing reaches the stats rollup)
    /// and shows <paramref name="outcomeHint"/> instead. For launches whose client never attached
    /// ("Never connected"). Until 2026-08-30 that case went through <see cref="MarkEndedAsync"/> with
    /// the failure time as the end, which handed a 30-120 s phantom session to the v1.23 uptime
    /// numbers. No-op if the row's been pruned.
    /// </summary>
    Task MarkOutcomeAsync(Guid sessionId, string outcomeHint);

    /// <summary>Drop everything. UI surfaces this behind a confirmation dialog.</summary>
    Task ClearAsync();
}
