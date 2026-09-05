using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// The uptime-mark state machine (Este, 2026-09-05): one mark per two hours of CONTINUOUS
/// running time, anchored when the running count leaves zero, reset when it returns, and never
/// a catch-up burst. Pure — the table needs no clock.
/// </summary>
public class UptimeMarkTrackerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void NothingRunning_NeverMarks()
    {
        var tracker = new UptimeMarkTracker();
        Assert.Null(tracker.Observe(T0, 0));
        Assert.Null(tracker.Observe(T0.AddHours(5), 0));
    }

    [Fact]
    public void FirstMark_AtTwoHours_NotBefore()
    {
        var tracker = new UptimeMarkTracker();
        Assert.Null(tracker.Observe(T0, 3));
        Assert.Null(tracker.Observe(T0.AddMinutes(119), 3));
        Assert.Equal(2, tracker.Observe(T0.AddHours(2), 3));
        Assert.Null(tracker.Observe(T0.AddMinutes(121), 3));
    }

    [Fact]
    public void SecondMark_AtFourHours()
    {
        var tracker = new UptimeMarkTracker();
        tracker.Observe(T0, 1);
        Assert.Equal(2, tracker.Observe(T0.AddHours(2), 1));
        Assert.Null(tracker.Observe(T0.AddHours(3), 1));
        Assert.Equal(4, tracker.Observe(T0.AddHours(4), 1));
    }

    [Fact]
    public void ZeroRunning_ResetsTheAnchor()
    {
        // Continuous is the promise: everything stopping ends the run, and the next launch
        // starts the clock over rather than inheriting the dead run's hours.
        var tracker = new UptimeMarkTracker();
        tracker.Observe(T0, 2);
        Assert.Equal(2, tracker.Observe(T0.AddHours(2), 2));
        Assert.Null(tracker.Observe(T0.AddHours(3), 0));
        Assert.Null(tracker.Observe(T0.AddHours(4), 2));
        Assert.Null(tracker.Observe(T0.AddHours(5), 2));
        Assert.Equal(2, tracker.Observe(T0.AddHours(6), 2));
    }

    [Fact]
    public void SleptThroughIntervals_OneCatchUpMark_NoBurst()
    {
        // A PC that dozed through three boundaries announces the latest one once. Three stacked
        // "still alive" pages would spend the reader's attention on exactly nothing.
        var tracker = new UptimeMarkTracker();
        tracker.Observe(T0, 4);
        Assert.Equal(6, tracker.Observe(T0.AddHours(6.5), 4));
        Assert.Null(tracker.Observe(T0.AddHours(7), 4));
        Assert.Equal(8, tracker.Observe(T0.AddHours(8), 4));
    }
}
