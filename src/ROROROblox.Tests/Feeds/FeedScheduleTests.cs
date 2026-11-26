using ROROROblox.App.Feeds;

namespace ROROROblox.Tests.Feeds;

/// <summary>Every "when" rule of the feeds coordinator, as the pure function it is: no timer, no clock.</summary>
public class FeedScheduleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly FeedOptions O = FeedOptions.Default;

    private static FeedClock Shown(
        DateTimeOffset? update = null,
        DateTimeOffset? status = null,
        bool strap = false,
        bool statusEnabled = true,
        bool refused = false,
        bool early = false,
        bool visible = true,
        DateTimeOffset? firstVisible = null,
        DateTimeOffset? statusEnabledAt = null) =>
        new(firstVisible ?? T0, visible, strap, statusEnabled, update, status, refused, early, statusEnabledAt);

    [Fact]
    public void TheDefaults_AreTheNumbersInTheSpec()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), O.Tick);
        Assert.Equal(TimeSpan.FromSeconds(30), O.SettleAfterShown);
        Assert.Equal(TimeSpan.FromMinutes(30), O.UpdateInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), O.StatusInterval);
        Assert.Equal(TimeSpan.FromMinutes(60), O.StatusRefusedInterval);
        Assert.Equal(TimeSpan.FromSeconds(60), O.StatusEarlyReadSpacing);
        Assert.Equal(TimeSpan.FromMinutes(30), O.StatusTrust);
    }

    [Fact]
    public void NothingIsDue_UntilThirtySecondsAfterTheWindowIsFirstVisible()
    {
        Assert.Equal(FeedDue.None, FeedSchedule.Decide(T0.AddSeconds(29), Shown(), O));
        Assert.Equal(FeedDue.Update | FeedDue.Status, FeedSchedule.Decide(T0.AddSeconds(30), Shown(), O));
    }

    [Fact]
    public void NothingIsDue_WhileTheWindowHasNeverBeenVisible()
    {
        var clock = new FeedClock(null, false, false, true, null, null, false, false);

        Assert.Equal(FeedDue.None, FeedSchedule.Decide(T0.AddDays(1), clock, O));
    }

    [Fact]
    public void NothingIsDue_WhileTheWindowIsNotVisible_EvenLongAfterTheSettle()
    {
        var hidden = Shown(visible: false);

        Assert.Equal(FeedDue.None, FeedSchedule.Decide(T0.AddHours(5), hidden, O));
        Assert.Equal(FeedDue.None, FeedSchedule.Decide(T0.AddHours(5), Shown(visible: false, early: true), O));
    }

    [Fact]
    public void Update_IsDueEveryThirtyMinutes()
    {
        var last = T0.AddSeconds(30);
        var clock = Shown(update: last, status: last);

        Assert.False(FeedSchedule.Decide(last.AddMinutes(29).AddSeconds(59), clock, O).HasFlag(FeedDue.Update));
        Assert.True(FeedSchedule.Decide(last.AddMinutes(30), clock, O).HasFlag(FeedDue.Update));
    }

    [Fact]
    public void Update_IsNeverDueUnderAStrap()
    {
        var due = FeedSchedule.Decide(T0.AddDays(2), Shown(strap: true), O);

        Assert.False(due.HasFlag(FeedDue.Update));
        Assert.True(due.HasFlag(FeedDue.Status));
    }

    [Fact]
    public void Status_IsDueEveryTenMinutes()
    {
        var last = T0.AddSeconds(30);
        var clock = Shown(update: last, status: last);

        Assert.False(FeedSchedule.Decide(last.AddMinutes(9).AddSeconds(59), clock, O).HasFlag(FeedDue.Status));
        Assert.True(FeedSchedule.Decide(last.AddMinutes(10), clock, O).HasFlag(FeedDue.Status));
    }

    [Fact]
    public void Status_AfterARefusal_WaitsSixtyMinutes()
    {
        var last = T0.AddSeconds(30);
        var clock = Shown(update: last, status: last, refused: true);

        Assert.False(FeedSchedule.Decide(last.AddMinutes(59).AddSeconds(59), clock, O).HasFlag(FeedDue.Status));
        Assert.True(FeedSchedule.Decide(last.AddMinutes(60), clock, O).HasFlag(FeedDue.Status));
    }

    [Fact]
    public void Status_AnEarlyRead_IsHonouredNoSoonerThanSixtySecondsAfterTheLastAttempt()
    {
        var last = T0.AddMinutes(5);
        var asked = Shown(update: last, status: last, early: true);
        var notAsked = Shown(update: last, status: last);

        Assert.False(FeedSchedule.Decide(last.AddSeconds(59), asked, O).HasFlag(FeedDue.Status));
        Assert.True(FeedSchedule.Decide(last.AddSeconds(60), asked, O).HasFlag(FeedDue.Status));
        Assert.False(FeedSchedule.Decide(last.AddSeconds(60), notAsked, O).HasFlag(FeedDue.Status));
    }

    [Fact]
    public void Status_AnEarlyRead_StillWaitsForTheSettleAndTheSwitch()
    {
        Assert.Equal(FeedDue.None, FeedSchedule.Decide(T0.AddSeconds(10), Shown(early: true), O));
        Assert.False(FeedSchedule.Decide(T0.AddHours(1), Shown(early: true, statusEnabled: false), O)
            .HasFlag(FeedDue.Status));
    }

    [Fact]
    public void Status_WhenSwitchedOff_IsNeverDue_ButUpdateStillIs()
    {
        var due = FeedSchedule.Decide(T0.AddHours(2), Shown(statusEnabled: false), O);

        Assert.Equal(FeedDue.Update, due);
    }

    [Fact]
    public void Status_SwitchedBackOn_SettlesAgainForThirtySeconds_WithoutHoldingUpdate()
    {
        var back = T0.AddHours(1);
        var clock = Shown(statusEnabledAt: back);

        Assert.Equal(FeedDue.Update, FeedSchedule.Decide(back.AddSeconds(29), clock, O));
        Assert.Equal(FeedDue.Update | FeedDue.Status, FeedSchedule.Decide(back.AddSeconds(30), clock, O));
    }

    [Fact]
    public void AClockThatMovedBackwards_DoesNotSilenceALaneUntilItCatchesUp()
    {
        var last = T0.AddHours(3);
        var clock = Shown(update: last, status: last);

        Assert.Equal(FeedDue.Update | FeedDue.Status, FeedSchedule.Decide(T0.AddHours(1), clock, O));
    }
}
