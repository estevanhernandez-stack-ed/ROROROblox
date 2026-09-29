using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

public class AutoRejoinMonitorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly ServerInstance Srv = new(5, "job-1");

    private static AutoRejoinCandidate C(bool inGame, bool running = true, bool enabled = true,
        bool isMain = false, bool stopping = false) =>
        new(Id, enabled, isMain, running, inGame, inGame ? Srv : null, stopping);

    private static IReadOnlyList<AutoRejoinAction> At(AutoRejoinMonitor m, double minutes, AutoRejoinCandidate c) =>
        m.Tick(T0.AddMinutes(minutes), [c]);

    [Fact]
    public void OutOfGameForThreeMinutes_WithTheClientRunning_Rejoins()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        Assert.Empty(At(m, 2.9, C(inGame: false)));
        var a = Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 3.0, C(inGame: false))));
        Assert.Equal(Srv, a.LastServer);
        Assert.False(a.FailedJoin);
    }

    [Fact]
    public void ClosedClient_IsNeverRejoined()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        Assert.Empty(At(m, 10, C(inGame: false, running: false)));
    }

    [Fact]
    public void ClosedByHandWhilePresenceStillSaidInGame_IsNeverRejoined()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        At(m, 1, C(inGame: true, running: false));
        Assert.Empty(At(m, 10, C(inGame: false, running: false)));
    }

    [Theory]
    [InlineData(false, false)] // not opted in
    [InlineData(true, true)]   // the main
    public void NotOptedInOrMain_IsNeverRejoined(bool enabled, bool isMain)
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true, enabled: enabled, isMain: isMain));
        Assert.Empty(At(m, 10, C(inGame: false, enabled: enabled, isMain: isMain)));
    }

    [Fact]
    public void StopInProgress_IsNotADrop()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        Assert.Empty(At(m, 5, C(inGame: false, stopping: true)));
    }

    [Fact]
    public void FirstJoin_GetsFiveMinutes_ThenCountsAsAFailedJoin()
    {
        var m = new AutoRejoinMonitor();
        m.NotifyLaunched(Id, T0);
        Assert.Empty(At(m, 4.9, C(inGame: false)));
        var a = Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 5.0, C(inGame: false))));
        Assert.True(a.FailedJoin);
    }

    [Fact]
    public void InFlight_BlocksASecondRejoinUntilTheLaunchIsNotified()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        Assert.Single(At(m, 3, C(inGame: false)));
        Assert.Empty(At(m, 7, C(inGame: false)));
        m.NotifyLaunched(Id, T0.AddMinutes(7));
        Assert.Empty(At(m, 11.9, C(inGame: false)));
        Assert.Single(At(m, 12, C(inGame: false)));
    }

    [Fact]
    public void FourthDropInAnHour_Pauses_AndResumeClearsIt()
    {
        var m = new AutoRejoinMonitor();
        double t = 0;
        for (var i = 0; i < 3; i++)
        {
            At(m, t, C(inGame: true));
            Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, t + 3, C(inGame: false))));
            m.NotifyLaunched(Id, T0.AddMinutes(t + 3));
            t += 4;
        }
        At(m, t, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Pause>(Assert.Single(At(m, t + 3, C(inGame: false))));
        Assert.True(m.IsPaused(Id));
        Assert.Empty(At(m, t + 10, C(inGame: false)));

        m.Resume(Id);
        Assert.False(m.IsPaused(Id));
    }

    [Fact]
    public void BudgetRefills_AfterAnHour()
    {
        var m = new AutoRejoinMonitor();
        for (var i = 0; i < 3; i++)
        {
            At(m, i * 4, C(inGame: true));
            At(m, i * 4 + 3, C(inGame: false));
            m.NotifyLaunched(Id, T0.AddMinutes(i * 4 + 3));
        }
        At(m, 70, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 73, C(inGame: false))));
    }

    [Fact]
    public void SkippedRejoin_RefundsTheBudget()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        for (var i = 0; i < 5; i++)
        {
            Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 3 + i, C(inGame: false))));
            m.NotifyRejoinSkipped(Id);
        }
        Assert.False(m.IsPaused(Id));
    }

    [Fact]
    public void DoubleNotifyRejoinSkipped_RefundsOnlyOnce_SoTheBudgetCapStillHolds()
    {
        var m = new AutoRejoinMonitor();

        // A confirmed rejoin — a real spent slot a later duplicate skip must not be able to touch.
        At(m, 0, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 3, C(inGame: false))));
        m.NotifyLaunched(Id, T0.AddMinutes(3));

        // A second rejoin, skipped twice in a row (duplicate/stale UI event). Only the first call
        // should refund anything; the second has no outstanding rejoin to refund.
        At(m, 4, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 7, C(inGame: false))));
        m.NotifyRejoinSkipped(Id);
        m.NotifyRejoinSkipped(Id);

        // Two more confirmed rejoins bring the real, counted total in the window to 3.
        At(m, 8, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 11, C(inGame: false))));
        m.NotifyLaunched(Id, T0.AddMinutes(11));

        At(m, 12, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 15, C(inGame: false))));
        m.NotifyLaunched(Id, T0.AddMinutes(15));

        Assert.False(m.IsPaused(Id));

        // A 4th drop: if the duplicate skip above had wrongly refunded the first confirmed slot
        // too, this would come back as a 4th Rejoin instead of a Pause.
        At(m, 16, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Pause>(Assert.Single(At(m, 19, C(inGame: false))));
        Assert.True(m.IsPaused(Id));

        // And a "5th" never sneaks through either — a paused account is simply skipped.
        Assert.Empty(At(m, 30, C(inGame: false)));
    }

    [Fact]
    public void ClearInFlight_LetsTheNextTickActAgain_WithoutRefundingTheSlot()
    {
        var m = new AutoRejoinMonitor();

        // Three rejoins whose client was stopped but whose relaunch didn't land: each one is
        // cleared in-flight (so the account isn't stuck) but still counts.
        for (var i = 0; i < 3; i++)
        {
            At(m, 10 * i, C(inGame: true));
            Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 10 * i + 3, C(inGame: false))));
            m.ClearInFlight(Id);
        }

        // Not stuck: the 4th drop is acted on, and as a Pause, because nothing was refunded.
        At(m, 30, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Pause>(Assert.Single(At(m, 33, C(inGame: false))));
    }

    [Fact]
    public void ClearInFlight_WhenNothingIsInFlight_IsANoOp()
    {
        var m = new AutoRejoinMonitor();
        m.ClearInFlight(Id); // unknown id
        At(m, 0, C(inGame: true));
        m.ClearInFlight(Id); // known, not in flight
        Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 3, C(inGame: false))));
    }

    [Fact]
    public void Pause_SkipsTheAccountUntilResume()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        m.Pause(Id);
        Assert.True(m.IsPaused(Id));
        Assert.Empty(At(m, 10, C(inGame: false)));

        m.Resume(Id);
        Assert.False(m.IsPaused(Id));
        At(m, 11, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 14, C(inGame: false))));
    }

    [Fact]
    public void Pause_WhileARejoinIsInFlight_ClearsIt_AndStillSkips()
    {
        var m = new AutoRejoinMonitor();
        At(m, 0, C(inGame: true));
        Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 3, C(inGame: false))));
        m.Pause(Id);
        Assert.Empty(At(m, 10, C(inGame: false)));
        m.Resume(Id);
        Assert.IsType<AutoRejoinAction.Rejoin>(Assert.Single(At(m, 11, C(inGame: false))));
    }
}
