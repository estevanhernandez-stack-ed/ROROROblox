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
    /// Mark a session ended. Looks up by id; no-op if the row's been pruned.
    /// </summary>
    Task MarkEndedAsync(Guid sessionId, DateTimeOffset endedAtUtc, string? outcomeHint = null);

    /// <summary>Drop everything. UI surfaces this behind a confirmation dialog.</summary>
    Task ClearAsync();
}
