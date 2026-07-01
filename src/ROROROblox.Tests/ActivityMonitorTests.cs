using System;
using System.Collections.Generic;
using System.Linq;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class ActivityMonitorTests
{
    // ---- hand-rolled fakes ----
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeForeground : IForegroundWindowProbe
    {
        public int? Pid;
        public bool TryGetForegroundPid(out int pid)
        {
            pid = Pid ?? 0;
            return Pid is > 0;
        }
    }

    private sealed class FakeInput : ISystemInputClock
    {
        public uint Tick;
        public uint LastInputTick => Tick;
    }

    private sealed class FakeResolver : IForegroundAccountResolver
    {
        public readonly Dictionary<int, Guid> Map = new();
        public bool TryResolveAccountByPid(int pid, out Guid accountId)
            => Map.TryGetValue(pid, out accountId);
    }

    private static (ActivityMonitor m, FakeClock clock, FakeForeground fg, FakeInput input, FakeResolver res)
        Build()
    {
        var clock = new FakeClock();
        var fg = new FakeForeground();
        var input = new FakeInput();
        var res = new FakeResolver();
        var m = new ActivityMonitor(fg, input, res, clock);
        return (m, clock, fg, input, res);
    }

    [Fact]
    public void Sample_InputAdvancedWhileAccountForeground_StampsThatAccount()
    {
        var (m, clock, fg, input, res) = Build();
        var a = Guid.NewGuid();
        res.Map[1000] = a;
        m.OnAccountLaunched(a);

        // move clock forward, put A foreground, advance input tick
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        fg.Pid = 1000;
        input.Tick = 42;
        m.Sample();

        var snap = m.GetSnapshot().Single(x => x.AccountId == a);
        Assert.Equal(clock.UtcNow, snap.LastActivityAt);
        Assert.Equal(TimeSpan.Zero, snap.SinceActivity);
    }

    [Fact]
    public void Sample_ForegroundNotTracked_StampsNobody()
    {
        var (m, clock, fg, input, res) = Build();
        var a = Guid.NewGuid();
        m.OnAccountLaunched(a);              // A launched, seeded at 12:00
        var seeded = m.GetSnapshot().Single(x => x.AccountId == a).LastActivityAt;

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        fg.Pid = 7777;                        // unknown pid (not in resolver)
        input.Tick = 99;
        m.Sample();

        var after = m.GetSnapshot().Single(x => x.AccountId == a).LastActivityAt;
        Assert.Equal(seeded, after);          // unchanged
    }

    [Fact]
    public void Sample_ForegroundButInputDidNotAdvance_AgesForegroundAccount()
    {
        var (m, clock, fg, input, res) = Build();
        var a = Guid.NewGuid();
        res.Map[1000] = a;
        m.OnAccountLaunched(a);               // seeded at 12:00
        input.Tick = 10;
        m.Sample();                            // first real sample establishes baseline tick

        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        fg.Pid = 1000;                         // A is foreground...
        // input.Tick stays 10 -> no advance (AFK on the foreground window)
        m.Sample();

        var snap = m.GetSnapshot().Single(x => x.AccountId == a);
        Assert.True(snap.SinceActivity >= TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Sample_FirstSampleAfterIdleLaunch_DoesNotFalselyStamp()
    {
        var (m, clock, fg, input, res) = Build();   // FakeInput.Tick defaults to 0 == ctor baseline
        var a = Guid.NewGuid();
        res.Map[1000] = a;
        m.OnAccountLaunched(a);                       // seeded at 12:00
        var seeded = m.GetSnapshot().Single().LastActivityAt;

        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        fg.Pid = 1000;                                // A is foreground at the first sample...
        // input.Tick stays at the construction baseline (0): user has been idle -> no advance
        m.Sample();

        var after = m.GetSnapshot().Single();
        Assert.Equal(seeded, after.LastActivityAt);   // NOT re-stamped
        Assert.True(after.SinceActivity >= TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void OnAccountExited_DropsRecord()
    {
        var (m, _, _, _, _) = Build();
        var a = Guid.NewGuid();
        m.OnAccountLaunched(a);
        Assert.Single(m.GetSnapshot());

        m.OnAccountExited(a);
        Assert.Empty(m.GetSnapshot());
    }

    [Fact]
    public void Sample_CrossingThreshold_FiresOnceThenLatches()
    {
        var (m, clock, fg, input, res) = Build();
        m.WarnThreshold = TimeSpan.FromMinutes(15);
        var a = Guid.NewGuid();
        m.OnAccountLaunched(a);               // seeded at 12:00

        var fires = new List<IReadOnlyList<Guid>>();
        m.WarnThresholdCrossed += (_, batch) => fires.Add(batch);

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        m.Sample();                            // crosses -> fires
        m.Sample();                            // still over, latched -> no fire

        Assert.Single(fires);
        Assert.Equal(a, Assert.Single(fires[0]));
    }

    [Fact]
    public void Sample_ReArmsAfterGoingActiveAgain()
    {
        var (m, clock, fg, input, res) = Build();
        m.WarnThreshold = TimeSpan.FromMinutes(15);
        var a = Guid.NewGuid();
        res.Map[1000] = a;
        m.OnAccountLaunched(a);
        input.Tick = 5; m.Sample();            // baseline

        var fires = new List<IReadOnlyList<Guid>>();
        m.WarnThresholdCrossed += (_, batch) => fires.Add(batch);

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        m.Sample();                            // fire #1 (latched)

        // becomes active again: foreground + input advance
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        fg.Pid = 1000; input.Tick = 6;
        m.Sample();                            // stamps -> since resets -> un-latches

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        m.Sample();                            // fire #2

        Assert.Equal(2, fires.Count);
    }

    [Fact]
    public void Sample_CoalescesMultipleCrossingsIntoOneBatch()
    {
        var (m, clock, fg, input, res) = Build();
        m.WarnThreshold = TimeSpan.FromMinutes(15);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        m.OnAccountLaunched(a);
        m.OnAccountLaunched(b);

        var fires = new List<IReadOnlyList<Guid>>();
        m.WarnThresholdCrossed += (_, batch) => fires.Add(batch);

        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        m.Sample();

        Assert.Single(fires);
        Assert.Equal(2, fires[0].Count);
    }
}
