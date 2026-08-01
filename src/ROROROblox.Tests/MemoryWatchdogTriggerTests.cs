using System;
using System.Collections.Generic;
using System.Linq;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class MemoryWatchdogTriggerTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan d) => UtcNow += d;
    }

    private sealed class FakeProcessMemory : IProcessMemoryProbe
    {
        public readonly Dictionary<int, long?> Readings = new();
        public bool TryReadPrivateBytes(int pid, out long privateBytes)
        {
            privateBytes = 0;
            if (!Readings.TryGetValue(pid, out var v) || v is null) return false;
            privateBytes = v.Value;
            return true;
        }
    }

    private sealed class FakeSystemMemory : ISystemMemoryProbe
    {
        public long Total = 32L * Gb;
        public long Available = 20L * Gb;
        public bool Ok = true;
        public bool TryRead(out long total, out long available)
        {
            total = Total; available = Available;
            return Ok;
        }
    }

    [Fact]
    public void CapCrossed_FiresOnceAndLatches()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory();
        var wd = new MemoryWatchdog(proc, sys, clock) { CapBytes = 4 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();
        Assert.Equal(0, fires);

        proc.Readings[10] = 5 * Gb; // over cap
        wd.Sample();
        Assert.Equal(1, fires);

        wd.Sample();                // still over — must NOT re-fire
        Assert.Equal(1, fires);
    }

    [Fact]
    public void CapCleared_ReArmsSoNextCrossingFiresAgain()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock) { CapBytes = 4 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 5 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();
        Assert.Equal(1, fires);

        proc.Readings[10] = 1 * Gb; // recycled — clears and re-arms
        wd.Sample();
        proc.Readings[10] = 5 * Gb;
        wd.Sample();
        Assert.Equal(2, fires);
    }

    [Fact]
    public void ProjectionCrossed_FiresWhenHeadroomRunsShort()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory { Available = 2 * Gb };
        var wd = new MemoryWatchdog(proc, sys, clock)
        {
            CapBytes = 0,                 // cap disabled — isolate the projection trigger
            ReserveBytes = 1 * Gb,
            ProjectionWarnMinutes = 120,
        };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(1));
        proc.Readings[10] = 4 * Gb;   // 2 GB/hr; 1 GB usable headroom => 30 min
        wd.Sample();

        Assert.Equal(1, fires);
        Assert.True(wd.GetSnapshot().MinutesToCeiling < 120);
    }

    [Fact]
    public void ZeroAggregateGrowth_ProducesNoProjectionAndNoDivideByZero()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory { Available = 1 * Gb }, clock)
        {
            CapBytes = 0,
            ProjectionWarnMinutes = 120,
        };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(1));
        wd.Sample();                  // flat — no growth

        Assert.False(wd.GetSnapshot().HasProjection);
        Assert.Equal(0, fires);
    }

    [Fact]
    public void Target_IsTheFattestClient()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock) { CapBytes = 3 * Gb };

        var small = Guid.NewGuid();
        var fat = Guid.NewGuid();
        proc.Readings[10] = 1 * Gb;
        proc.Readings[20] = 6 * Gb;
        wd.OnAccountLaunched(small, 10);
        wd.OnAccountLaunched(fat, 20);
        wd.Sample();

        Assert.Equal(fat, wd.GetSnapshot().TargetAccountId);
        Assert.True(wd.GetSnapshot().Accounts.Single(a => a.AccountId == fat).IsTarget);
    }

    [Fact]
    public void Target_ExcludesUnreadableAccount_EvenIfItWasTheFattest()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock);

        var big = Guid.NewGuid();
        var small = Guid.NewGuid();
        proc.Readings[10] = 6 * Gb;   // big — currently the fattest, readable
        proc.Readings[20] = 1 * Gb;   // small — readable throughout
        wd.OnAccountLaunched(big, 10);
        wd.OnAccountLaunched(small, 20);
        wd.Sample();

        // big goes unreadable. AccountMemory still reports its last-known-good 6 GB as
        // PrivateBytes (rec.LastBytes), so if the .Where(a => a.ReadOk) filter were dropped,
        // ordering-by-bytes alone would still pick big as the target.
        proc.Readings[10] = null;
        wd.Sample();

        Assert.Equal(small, wd.GetSnapshot().TargetAccountId);
        Assert.False(wd.GetSnapshot().Accounts.Single(a => a.AccountId == big).IsTarget);
        Assert.True(wd.GetSnapshot().Accounts.Single(a => a.AccountId == small).IsTarget);
    }

    [Fact]
    public void PressureCrossed_FiresOnceWhenTwoAccountsCrossOnSameSample()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock) { CapBytes = 3 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var a1 = Guid.NewGuid();
        var a2 = Guid.NewGuid();
        proc.Readings[10] = 1 * Gb;
        proc.Readings[20] = 1 * Gb;
        wd.OnAccountLaunched(a1, 10);
        wd.OnAccountLaunched(a2, 20);
        wd.Sample();
        Assert.Equal(0, fires);

        proc.Readings[10] = 5 * Gb; // both cross the cap on the same tick
        proc.Readings[20] = 6 * Gb;
        wd.Sample();

        Assert.Equal(1, fires); // must coalesce into a single event, not fire per account
    }

    [Fact]
    public void SystemReadFails_SkipsProjectionButStillEvaluatesCap()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory { Ok = false };
        var wd = new MemoryWatchdog(proc, sys, clock) { CapBytes = 4 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 5 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();

        Assert.False(wd.GetSnapshot().HasProjection);
        Assert.Equal(1, fires); // cap still fired
    }

    [Fact]
    public void ResetBaseline_ClearsBothLatches()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock) { CapBytes = 4 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 5 * Gb;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();
        Assert.Equal(1, fires);

        wd.ResetBaseline(id, pid: 11); // recycled: new pid, fresh baseline, latches cleared
        proc.Readings[11] = 5 * Gb;
        wd.Sample();
        Assert.Equal(2, fires);
    }
}
