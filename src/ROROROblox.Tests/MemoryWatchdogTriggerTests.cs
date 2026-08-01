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

    [Fact]
    public void UnreadablePid_DoesNotClearASetCapLatch()
    {
        // IMPORTANT 3 (final-branch review, 2026-08-01): an unreadable pid is UNKNOWN, not
        // "clear" -- TryReadPrivateBytes fails routinely in normal operation (process mid-teardown,
        // transient access denied). If a flaky read cleared the cap latch, an over-cap client would
        // re-cross and re-balloon every 60s for hours on an unattended 20+-hour session.
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock) { CapBytes = 4 * Gb };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 5 * Gb; // over cap
        wd.OnAccountLaunched(id, 10);
        wd.Sample();
        Assert.Equal(1, fires);

        proc.Readings[10] = null; // read fails this tick -- UNKNOWN, not clear
        wd.Sample();

        // Discriminator: if the read failure wrongly cleared the latch, this still-over-cap
        // reading would re-fire (fires == 2). It must not.
        proc.Readings[10] = 5 * Gb; // still over cap, readable again
        wd.Sample();
        Assert.Equal(1, fires);
    }

    [Fact]
    public void SystemReadFailure_DoesNotClearASetProjectionLatch()
    {
        // IMPORTANT 3 (final-branch review, 2026-08-01): a failed GlobalMemoryStatusEx read is
        // UNKNOWN, not "clear" -- clearing every account's projection latch on a transient system
        // read failure re-fires the projection warning every 60s for hours. The spec's own
        // failure-mode table says a failed availPhys read must SKIP projection evaluation;
        // clearing the latch is evaluating it.
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory { Available = 2 * Gb };
        var wd = new MemoryWatchdog(proc, sys, clock)
        {
            CapBytes = 0, // isolate the projection trigger
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
        proc.Readings[10] = 4 * Gb; // 2 GB/hr; 1 GB usable headroom => 30 min, crosses
        wd.Sample();
        Assert.Equal(1, fires);
        Assert.True(wd.GetSnapshot().MinutesToCeiling < 120);

        sys.Ok = false; // system read fails this tick -- UNKNOWN, not clear
        clock.Advance(TimeSpan.FromMinutes(1));
        proc.Readings[10] = 4 * Gb + Gb / 32; // still growing, still would cross if evaluated
        wd.Sample();
        Assert.False(wd.GetSnapshot().HasProjection); // failed read -> no projection this tick

        // Discriminator: if the failed read wrongly cleared the latch, restoring the system read
        // with an unchanged over-projection reading would re-fire (fires == 2). It must not.
        sys.Ok = true;
        clock.Advance(TimeSpan.FromMinutes(1));
        wd.Sample();
        Assert.Equal(1, fires);
    }

    [Fact]
    public void CapOscillatesAcrossBoundary_FiresExactlyOnce()
    {
        // Live smoke test: one account crossed the cap FOUR times in eight minutes -- 2657,
        // 2643, 2642, 2653 MB against a 2640 MB cap, with small dips (13-25 MB, real client
        // oscillation amplitude) between each crossing. A bare `!overCap` re-arm treats every
        // one of those dips as "cleared" and re-fires on the very next tick -- this is the
        // sequence that produces the four-balloons-in-eight-minutes bug. CapReArmFactor (0.95)
        // means none of these dips (all comfortably above 2640*0.95 = 2508 MB) re-arm the latch.
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock) { CapBytes = 2640L * 1024 * 1024 };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 2000L * 1024 * 1024; // under cap -- seeds baseline
        wd.OnAccountLaunched(id, 10);
        wd.Sample();
        Assert.Equal(0, fires);

        long Mb(long mb) => mb * 1024 * 1024;
        foreach (var reading in new[] { 2657L, 2635L, 2643L, 2638L, 2642L, 2625L, 2653L })
        {
            proc.Readings[10] = Mb(reading);
            wd.Sample();
        }

        Assert.Equal(1, fires); // must not have re-fired on any of the small dips
    }

    [Fact]
    public void CapDropsWellBelowDeadband_ReArmsSoLaterCrossingFiresAgain()
    {
        // The guard against over-correcting: a genuine Recycle (~2900 MB -> ~1700 MB, well past
        // the 2508 MB deadband for a 2640 MB cap) must still re-arm the latch, or a real second
        // crossing would silently go unreported.
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var wd = new MemoryWatchdog(proc, new FakeSystemMemory(), clock) { CapBytes = 2640L * 1024 * 1024 };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        proc.Readings[10] = 2000L * 1024 * 1024;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();

        proc.Readings[10] = 2900L * 1024 * 1024; // crosses
        wd.Sample();
        Assert.Equal(1, fires);

        proc.Readings[10] = 1700L * 1024 * 1024; // Recycle: well below deadband -- re-arms
        wd.Sample();

        proc.Readings[10] = 2900L * 1024 * 1024; // genuine second crossing
        wd.Sample();
        Assert.Equal(2, fires);
    }

    [Fact]
    public void ProjectionOscillatesAcrossThreshold_FiresExactlyOnce()
    {
        // Live smoke test: the projection axis crossed at 85 min, cleared, crossed again at
        // 113 min eleven minutes later. ProjectionReArmFactor (1.15) means recovery must clear
        // the 120-min warn threshold by 15% (138 min) before the latch re-arms, so a value
        // hovering just above the raw threshold does not re-fire every tick.
        const double Gb = 1024.0 * 1024 * 1024;
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory();
        var wd = new MemoryWatchdog(proc, sys, clock)
        {
            CapBytes = 0, // isolate the projection trigger
            ReserveBytes = 0,
            ProjectionWarnMinutes = 120,
        };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        var baseline = (long)(2 * Gb);
        proc.Readings[10] = baseline;
        wd.OnAccountLaunched(id, 10);
        wd.Sample(); // seeds baseline; elapsed 0 -> no projection yet
        Assert.Equal(0, fires);

        const double growthPerHour = 1 * Gb; // held exactly constant via baseline + rate*elapsed below

        // 15 min in: minutes ~= 100 -- crosses (< 120).
        clock.Advance(TimeSpan.FromMinutes(15));
        proc.Readings[10] = (long)(baseline + growthPerHour * (15.0 / 60));
        sys.Available = (long)(growthPerHour * 100 / 60);
        wd.Sample();
        Assert.Equal(1, fires);
        Assert.True(wd.GetSnapshot().MinutesToCeiling < 120);

        // 30 min in: minutes ~= 130 -- clears the raw `< 120` condition but stays inside the
        // 138-min deadband. Must NOT re-arm.
        clock.Advance(TimeSpan.FromMinutes(15));
        proc.Readings[10] = (long)(baseline + growthPerHour * (30.0 / 60));
        sys.Available = (long)(growthPerHour * 130 / 60);
        wd.Sample();
        Assert.Equal(1, fires);

        // 45 min in: minutes ~= 110 -- flaps back under the warn threshold. Latch was never
        // cleared, so this must NOT re-fire either.
        clock.Advance(TimeSpan.FromMinutes(15));
        proc.Readings[10] = (long)(baseline + growthPerHour * (45.0 / 60));
        sys.Available = (long)(growthPerHour * 110 / 60);
        wd.Sample();
        Assert.Equal(1, fires);
    }

    [Fact]
    public void ProjectionRecoversPastDeadband_ReArmsSoLaterCrossingFiresAgain()
    {
        const double Gb = 1024.0 * 1024 * 1024;
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory();
        var wd = new MemoryWatchdog(proc, sys, clock)
        {
            CapBytes = 0,
            ReserveBytes = 0,
            ProjectionWarnMinutes = 120,
        };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        var baseline = (long)(2 * Gb);
        proc.Readings[10] = baseline;
        wd.OnAccountLaunched(id, 10);
        wd.Sample();

        const double growthPerHour = 1 * Gb;

        clock.Advance(TimeSpan.FromMinutes(15));
        proc.Readings[10] = (long)(baseline + growthPerHour * (15.0 / 60));
        sys.Available = (long)(growthPerHour * 100 / 60); // minutes ~= 100 -- crosses
        wd.Sample();
        Assert.Equal(1, fires);

        // Genuine recovery: minutes climbs to ~150, well past the 138-min deadband -> re-arms.
        clock.Advance(TimeSpan.FromMinutes(15));
        proc.Readings[10] = (long)(baseline + growthPerHour * (30.0 / 60));
        sys.Available = (long)(growthPerHour * 150 / 60);
        wd.Sample();
        Assert.Equal(1, fires); // re-arming itself must not fire

        // Later genuine crossing must fire again.
        clock.Advance(TimeSpan.FromMinutes(15));
        proc.Readings[10] = (long)(baseline + growthPerHour * (45.0 / 60));
        sys.Available = (long)(growthPerHour * 90 / 60); // minutes ~= 90 -- crosses again
        wd.Sample();
        Assert.Equal(2, fires);
    }

    [Fact]
    public void ProjectionPlateausToZeroGrowth_ReArmsSoLaterCrossingFiresAgain()
    {
        // Coordinator-caught bug in the first hysteresis pass: `hasProjection = systemOk &&
        // aggregateGrowth > 0`, so the instant growth plateaus to zero, `minutes` is forced to
        // 0 by the code above. The literal re-arm formula `minutes > ProjectionWarnMinutes *
        // ProjectionReArmFactor` can then NEVER be true on a plateau -- a latched account would
        // stay latched forever, and a later GENUINE crossing would silently not re-fire. That is
        // a missed warning, strictly worse than the spurious re-fires the deadband exists to
        // prevent. The fix adds a second, narrower re-arm path: `aggregateGrowth <= 0` is a
        // KNOWN clear (flat/shrinking, unambiguously healthy, no deadband needed since there's
        // no threshold to flap around) -- distinct from `systemOk == false`, which stays UNKNOWN
        // and must not re-arm (final-branch review IMPORTANT 3, untouched by this test).
        const double Gb = 1024.0 * 1024 * 1024;
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory();
        var wd = new MemoryWatchdog(proc, sys, clock)
        {
            CapBytes = 0, // isolate the projection trigger
            ReserveBytes = 0,
            ProjectionWarnMinutes = 120,
        };
        var fires = 0;
        wd.PressureCrossed += (_, _) => fires++;

        var id = Guid.NewGuid();
        var baseline = (long)(2 * Gb);
        proc.Readings[10] = baseline;
        wd.OnAccountLaunched(id, 10);
        wd.Sample(); // seeds baseline; elapsed 0 -> no projection yet

        const double growthPerHour = 1 * Gb;

        // 15 min in: minutes ~= 100 -- crosses (< 120).
        clock.Advance(TimeSpan.FromMinutes(15));
        proc.Readings[10] = (long)(baseline + growthPerHour * (15.0 / 60));
        sys.Available = (long)(growthPerHour * 100 / 60);
        wd.Sample();
        Assert.Equal(1, fires);

        // 30 min in: bytes settle back to EXACTLY the baseline (not below it -- that would
        // ratchet and reset elapsed, muddying what this test isolates). growth = (bytes -
        // baseline) / elapsed = 0 exactly. hasProjection goes false; systemOk stays true. This
        // is the "known clear" plateau, not the "unknown" system-read-failure case.
        clock.Advance(TimeSpan.FromMinutes(15));
        proc.Readings[10] = baseline;
        wd.Sample();
        Assert.False(wd.GetSnapshot().HasProjection);
        Assert.Equal(1, fires); // the plateau itself must not fire

        // 45 min in: growth resumes from the SAME baseline/baselineAt (no ratchet happened
        // above) -- a genuine fresh crossing. If the plateau tick above failed to re-arm the
        // latch, this must NOT fire; the whole point of this test is that it DOES.
        clock.Advance(TimeSpan.FromMinutes(15));
        proc.Readings[10] = (long)(baseline + growthPerHour * (45.0 / 60));
        sys.Available = (long)(growthPerHour * 90 / 60); // minutes ~= 90 -- crosses again
        wd.Sample();
        Assert.Equal(2, fires);
    }
}
