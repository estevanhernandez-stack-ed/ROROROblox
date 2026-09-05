namespace ROROROblox.Core.Discord;

/// <summary>
/// Decides when an uptime mark is due (Este, 2026-09-05): one mark per
/// <see cref="MarkInterval"/> of CONTINUOUS running time, measured from the moment the running
/// count left zero, reset the moment it returns to zero. Pure state-machine — the caller supplies
/// "now" and the count, so the whole table is unit-testable without a clock.
/// <para>
/// A PC that slept through several intervals gets ONE catch-up mark (the latest boundary), never
/// a burst — the marks' value is "still alive", and three stacked pages saying so would spend
/// the reader's attention on exactly nothing.
/// </para>
/// </summary>
public sealed class UptimeMarkTracker
{
    public static readonly TimeSpan MarkInterval = TimeSpan.FromHours(2);

    private DateTimeOffset? _anchorUtc;
    private int _marksRaised;

    /// <summary>
    /// Feed one observation; returns the whole-hour figure to announce ("4" for the 4h mark)
    /// when a new mark is due, else null.
    /// </summary>
    public int? Observe(DateTimeOffset nowUtc, int runningCount)
    {
        if (runningCount <= 0)
        {
            _anchorUtc = null;
            _marksRaised = 0;
            return null;
        }

        _anchorUtc ??= nowUtc;

        var completedIntervals = (int)((nowUtc - _anchorUtc.Value) / MarkInterval);
        if (completedIntervals <= _marksRaised)
        {
            return null;
        }

        _marksRaised = completedIntervals;
        return (int)(completedIntervals * MarkInterval.TotalHours);
    }
}
