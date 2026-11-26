namespace ROROROblox.App.Feeds;

/// <summary>Every interval the coordinator keeps, as values, so a test can build its own.</summary>
internal sealed record FeedOptions(
    TimeSpan Tick,
    TimeSpan SettleAfterShown,
    TimeSpan UpdateInterval,
    TimeSpan StatusInterval,
    TimeSpan StatusRefusedInterval,
    TimeSpan StatusEarlyReadSpacing,
    TimeSpan StatusTrust)
{
    public static FeedOptions Default { get; } = new(
        Tick: TimeSpan.FromSeconds(30),
        SettleAfterShown: TimeSpan.FromSeconds(30),
        UpdateInterval: TimeSpan.FromMinutes(30),
        StatusInterval: TimeSpan.FromMinutes(10),
        StatusRefusedInterval: TimeSpan.FromMinutes(60),
        StatusEarlyReadSpacing: TimeSpan.FromSeconds(60),
        StatusTrust: TimeSpan.FromMinutes(30));
}

/// <summary>
/// Everything <see cref="FeedSchedule.Decide"/> needs, as plain values. <c>StatusEnabledAt</c> is when
/// the status switch last came on (null: it never has been switched, so the window's own settle governs);
/// turning it back on restarts the settle for the status lane only, as if the window had just been shown.
/// </summary>
internal sealed record FeedClock(
    DateTimeOffset? FirstVisibleAt,
    bool Visible,
    bool StrapHandlesLaunches,
    bool StatusEnabled,
    DateTimeOffset? UpdateLastAttempt,
    DateTimeOffset? StatusLastAttempt,
    bool StatusLastRefused,
    bool StatusEarlyReadRequested,
    DateTimeOffset? StatusEnabledAt = null);

[Flags]
internal enum FeedDue
{
    None = 0,
    Update = 1,
    Status = 2,
}

/// <summary>When each lane may ask. A pure function: no timer, no clock, no state.</summary>
internal static class FeedSchedule
{
    public static FeedDue Decide(DateTimeOffset now, FeedClock clock, FeedOptions o)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(o);

        if (!clock.Visible || clock.FirstVisibleAt is not { } shown || !Passed(now, shown, o.SettleAfterShown))
        {
            return FeedDue.None;
        }

        var due = FeedDue.None;

        if (!clock.StrapHandlesLaunches
            && (clock.UpdateLastAttempt is not { } update || Passed(now, update, o.UpdateInterval)))
        {
            due |= FeedDue.Update;
        }

        if (clock.StatusEnabled
            && (clock.StatusEnabledAt is not { } enabled || Passed(now, enabled, o.SettleAfterShown))
            && StatusIsDue(now, clock, o))
        {
            due |= FeedDue.Status;
        }

        return due;
    }

    private static bool StatusIsDue(DateTimeOffset now, FeedClock clock, FeedOptions o)
    {
        if (clock.StatusLastAttempt is not { } last)
        {
            return true;
        }

        var interval = clock.StatusLastRefused ? o.StatusRefusedInterval : o.StatusInterval;
        if (Passed(now, last, interval))
        {
            return true;
        }

        return clock.StatusEarlyReadRequested && now - last >= o.StatusEarlyReadSpacing;
    }

    /// <summary>
    /// True once <paramref name="span"/> has gone by since <paramref name="since"/>. A clock that moved
    /// backwards also counts as passed, so one wall-clock correction cannot silence a lane for hours.
    /// </summary>
    private static bool Passed(DateTimeOffset now, DateTimeOffset since, TimeSpan span)
        => now < since || now - since >= span;
}
