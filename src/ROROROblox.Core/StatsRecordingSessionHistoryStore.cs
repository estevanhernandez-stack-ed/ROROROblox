using Microsoft.Extensions.Logging;

namespace ROROROblox.Core;

/// <summary>
/// Wraps <see cref="ISessionHistoryStore"/> and feeds the rollup as history is written.
///
/// <para>WHY A DECORATOR RATHER THAN A SECOND CALL. The obvious implementation is to call a stats
/// service from <c>MainViewModel</c> beside the existing history call. That creates two call sites
/// that must agree forever, which is exactly the shape of F-121: a fix landed at one site while a
/// second kept the old form, with a comment asserting they matched. Wrapping instead means there is
/// still exactly ONE path into history, so the stats cannot silently disagree with what History
/// shows — not because someone remembered to keep them in step, but because there is nothing to
/// keep in step.</para>
///
/// <para>THE INNER CALL ALWAYS WINS. History is the real data; stats are a convenience derived from
/// it. Every stats operation is wrapped so a stats failure degrades to a logged warning rather than
/// losing a history row. A swallowed inner call would be a data-loss bug wearing a feature's
/// clothes.</para>
/// </summary>
public sealed class StatsRecordingSessionHistoryStore : ISessionHistoryStore
{
    private readonly ISessionHistoryStore _inner;
    private readonly ISessionStatsStore _stats;
    private readonly ILogger<StatsRecordingSessionHistoryStore>? _log;

    public StatsRecordingSessionHistoryStore(
        ISessionHistoryStore inner,
        ISessionStatsStore stats,
        ILogger<StatsRecordingSessionHistoryStore>? log = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _log = log;
    }

    public Task<IReadOnlyList<LaunchSession>> ListAsync() => _inner.ListAsync();

    public async Task AddAsync(LaunchSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        await _inner.AddAsync(session).ConfigureAwait(false);

        await SafeAsync(() => _stats.ApplyAsync(new StatsEvent.LaunchRecorded(
            session.AccountId, session.PlaceId, session.GameName, session.LaunchedAtUtc)))
            .ConfigureAwait(false);
    }

    public async Task MarkEndedAsync(Guid sessionId, DateTimeOffset endedAtUtc, string? outcomeHint = null)
    {
        // The start time lives on the row, so read BEFORE delegating — MarkEnded does not return it
        // and the row could be pruned by a later write.
        LaunchSession? row = null;
        try
        {
            row = (await _inner.ListAsync().ConfigureAwait(false)).FirstOrDefault(s => s.Id == sessionId);
        }
        catch (Exception ex)
        {
            _log?.LogDebug(ex, "Could not read the session row before ending it; stats will count it as missing.");
        }

        await _inner.MarkEndedAsync(sessionId, endedAtUtc, outcomeHint).ConfigureAwait(false);

        await SafeAsync(() => row is null
            // The row fell off the 100-row window before its client closed. We cannot know how long
            // it ran, so count it rather than invent a duration (§5).
            ? _stats.ApplyAsync(new StatsEvent.SessionMissingEnd())
            : _stats.ApplyAsync(new StatsEvent.SessionEnded(
                row.AccountId, row.PlaceId, row.LaunchedAtUtc, endedAtUtc)))
            .ConfigureAwait(false);
    }

    public async Task ClearAsync()
    {
        await _inner.ClearAsync().ConfigureAwait(false);

        // Otherwise "clear my history" leaves lifetime totals standing, which reads as the
        // confirmation dialog having lied.
        await SafeAsync(() => _stats.ClearAsync()).ConfigureAwait(false);
    }

    private async Task SafeAsync(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "Session stats update failed; history is unaffected.");
        }
    }
}
