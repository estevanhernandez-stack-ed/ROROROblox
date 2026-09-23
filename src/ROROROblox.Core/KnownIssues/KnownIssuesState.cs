using Microsoft.Extensions.Logging;

namespace ROROROblox.Core.KnownIssues;

public enum KnownIssuesSource
{
    None,
    SavedCopy,
    Release,
}

/// <param name="CheckedAt">When the last download attempt finished, whatever its outcome; null until the first one does.</param>
/// <param name="LastOutcome">
/// What the last finished attempt concluded; null until one has finished. Loading the saved copy at
/// startup is not an attempt, so it leaves this null too — the page reads this, together with
/// <see cref="Source"/> and <see cref="CheckedAt"/>, to tell "empty and current" from "broken"
/// (<c>KnownIssuesStatusLine</c>, App project).
/// </param>
public sealed record KnownIssuesSnapshot(
    IReadOnlyList<KnownIssue> Issues, KnownIssuesSource Source, DateTimeOffset? CheckedAt, KnownIssuesRefreshKind? LastOutcome = null)
{
    public static readonly KnownIssuesSnapshot Empty = new([], KnownIssuesSource.None, null);
}

/// <summary>
/// The one live copy of the list. Singleton; the feed it drives is transient and stateless.
/// <para>
/// Every attempt ends with one Information line saying what happened. The metric path showed on
/// 2026-09-21 what a path that writes nothing costs: no incident on it could be reconstructed.
/// Failures keep the list and still stamp <see cref="KnownIssuesSnapshot.CheckedAt"/>, so the page's
/// "Last checked" line tells the truth about the attempt, not about the last success.
/// </para>
/// </summary>
public sealed class KnownIssuesState
{
    private readonly TimeProvider _time;
    private readonly ILogger<KnownIssuesState> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private KnownIssuesSnapshot _current = KnownIssuesSnapshot.Empty;

    public KnownIssuesState(TimeProvider time, ILogger<KnownIssuesState> log)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public KnownIssuesSnapshot Current => Volatile.Read(ref _current);

    /// <summary>Raised on the calling thread — for a refresh, a thread-pool thread. Listeners marshal.</summary>
    public event EventHandler<KnownIssuesSnapshot>? Changed;

    public void LoadSavedCopy(IKnownIssuesFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var document = feed.LoadCache();
        if (document is null)
        {
            return;
        }

        _log.LogInformation(
            "Known issues: {Count} entries, {NotifyCount} with a notice, from the saved copy.",
            document.Issues.Count,
            document.Issues.Count(i => i.Notify));
        Publish(new KnownIssuesSnapshot(document.Issues, KnownIssuesSource.SavedCopy, null));
    }

    public async Task<KnownIssuesRefreshKind> RefreshAsync(IKnownIssuesFeed feed, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            KnownIssuesFetch fetch;
            try
            {
                fetch = await feed.FetchAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // A genuine caller cancellation, not a feed failure -- propagate rather than
                // reporting a phantom "checked and failed" the caller never asked for.
                throw;
            }
            catch (Exception ex)
            {
                // IKnownIssuesFeed.FetchAsync is documented "never throws", but a caller who
                // trusted that alone would show "Last checked at 12:47 PM" for a check that never
                // finished -- the false all-clear this whole commit exists to close. Treated the
                // same as a network failure: keep the list, stamp the check, say so.
                var failedNow = _time.GetUtcNow();
                var failed = Current with { CheckedAt = failedNow, LastOutcome = KnownIssuesRefreshKind.NetworkFailed };
                _log.LogWarning(ex, "Known issues: the refresh failed unexpectedly; keeping what we have.");
                _log.LogInformation(
                    "Known issues: kept {Count} entries from {Source}: {Outcome}.",
                    failed.Issues.Count,
                    failed.Source,
                    KnownIssuesRefreshKind.NetworkFailed);
                Publish(failed);
                return KnownIssuesRefreshKind.NetworkFailed;
            }

            var now = _time.GetUtcNow();

            KnownIssuesSnapshot next;
            if (fetch.Kind == KnownIssuesRefreshKind.Updated && fetch.Document is { } document)
            {
                next = new KnownIssuesSnapshot(document.Issues, KnownIssuesSource.Release, now, fetch.Kind);
                _log.LogInformation(
                    "Known issues: {Count} entries, {NotifyCount} with a notice, from the release.",
                    next.Issues.Count,
                    next.Issues.Count(i => i.Notify));
            }
            else
            {
                next = Current with { CheckedAt = now, LastOutcome = fetch.Kind };
                _log.LogInformation(
                    "Known issues: kept {Count} entries from {Source}: {Outcome}.",
                    next.Issues.Count,
                    next.Source,
                    fetch.Kind);
            }

            Publish(next);
            return fetch.Kind;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Publish(KnownIssuesSnapshot snapshot)
    {
        Volatile.Write(ref _current, snapshot);
        Changed?.Invoke(this, snapshot);
    }
}
