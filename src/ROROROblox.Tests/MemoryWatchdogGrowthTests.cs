using System;
using System.Collections.Generic;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class MemoryWatchdogGrowthTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        public void Advance(TimeSpan d) => UtcNow += d;
    }

    private sealed class FakeProcessMemory : IProcessMemoryProbe
    {
        public readonly Dictionary<int, long?> Readings = new(); // null = unreadable
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
        public long Total = 32L * 1024 * 1024 * 1024;
        public long Available = 20L * 1024 * 1024 * 1024;
        public bool Ok = true;
        public bool TryRead(out long total, out long available)
        {
            total = Total; available = Available;
            return Ok;
        }
    }

    private const long Gb = 1024L * 1024 * 1024;

    private static (MemoryWatchdog wd, FakeClock clock, FakeProcessMemory proc, FakeSystemMemory sys) Build()
    {
        var clock = new FakeClock();
        var proc = new FakeProcessMemory();
        var sys = new FakeSystemMemory();
        var wd = new MemoryWatchdog(proc, sys, clock);
        return (wd, clock, proc, sys);
    }

    [Fact]
    public void Growth_IsBytesPerHourOverElapsed()
    {
        var (wd, clock, proc, _) = Build();
        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, pid: 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(2));
        proc.Readings[10] = 3 * Gb;           // +1 GB over 2 hours
        wd.Sample();

        var acct = Assert.Single(wd.GetSnapshot().Accounts);
        Assert.Equal(0.5 * Gb, acct.GrowthBytesPerHour, precision: 0);
    }

    [Fact]
    public void ObservationWindowUnmet_YieldsNoProjection()
    {
        var (wd, clock, proc, _) = Build();
        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, pid: 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromMinutes(5)); // under the 10-minute minimum
        proc.Readings[10] = 4 * Gb;
        wd.Sample();

        Assert.Equal(0, wd.GetSnapshot().MinutesToCeiling);
    }

    [Fact]
    public void ClientShrank_RatchetsBaselineAndRestartsWindow()
    {
        var (wd, clock, proc, _) = Build();
        var id = Guid.NewGuid();
        proc.Readings[10] = 5 * Gb;
        wd.OnAccountLaunched(id, pid: 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(1));
        proc.Readings[10] = 2 * Gb;  // teleport freed memory
        wd.Sample();

        // Baseline ratcheted to 2 GB. Right after a ratchet, bytes == BaselineBytes by
        // construction, so growth is 0 regardless of whether the window also restarted —
        // this assertion alone does not discriminate the window-restart behaviour.
        var acct = Assert.Single(wd.GetSnapshot().Accounts);
        Assert.Equal(0, acct.GrowthBytesPerHour, precision: 0);

        // Discriminator: advance LESS than MinimumObservation past the ratchet, with bytes grown.
        // If the window genuinely restarted at the ratchet (BaselineAt reset to the ratchet's
        // `now`), elapsed-since-ratchet is 5 minutes -- under the 10-minute floor -- so growth
        // must still read 0. If BaselineAt were NOT reset by the ratchet, elapsed would instead be
        // measured from the original launch an hour+ ago (already past the floor), and this
        // sample would wrongly report a nonzero slope before the post-ratchet window is satisfied.
        clock.Advance(TimeSpan.FromMinutes(5));
        proc.Readings[10] = 2 * Gb + Gb / 2; // grew since the ratchet, but window still unmet
        wd.Sample();

        var acctAfterPartialWindow = Assert.Single(wd.GetSnapshot().Accounts);
        Assert.Equal(0, acctAfterPartialWindow.GrowthBytesPerHour, precision: 0);
    }

    [Fact]
    public void UnreadablePid_IsExcludedFromAggregate_NotTreatedAsZero()
    {
        var (wd, clock, proc, _) = Build();
        var readable = Guid.NewGuid();
        var unreadable = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        proc.Readings[20] = 2 * Gb;
        wd.OnAccountLaunched(readable, pid: 10);
        wd.OnAccountLaunched(unreadable, pid: 20);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(1));
        proc.Readings[10] = 3 * Gb;   // +1 GB/hr
        proc.Readings[20] = null;     // now unreadable
        wd.Sample();

        var snap = wd.GetSnapshot();
        // Aggregate is the readable client's 1 GB/hr ONLY. A zero substituted for the
        // unreadable one would still be 1 GB/hr, so assert the flag too.
        Assert.Equal(1.0 * Gb, snap.AggregateGrowthBytesPerHour, precision: 0);
        Assert.False(Assert.Single(snap.Accounts, a => a.AccountId == unreadable).ReadOk);
    }

    [Fact]
    public void AccountExited_DropsTheRecord()
    {
        var (wd, _, proc, _) = Build();
        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, pid: 10);
        wd.Sample();

        wd.OnAccountExited(id);
        wd.Sample();

        Assert.Empty(wd.GetSnapshot().Accounts);
    }

    [Fact]
    public void NegativeElapsed_NeverProducesNegativeGrowth()
    {
        // NOTE: this does NOT isolate the elapsed-clamp specifically. With MinimumObservation as a
        // fixed positive 10-minute floor, a negative elapsed already fails the
        // `elapsed >= MinimumObservation` gate on its own (whether or not it was first clamped to
        // zero) -- growth simply stays at its 0 default. So this test cannot distinguish "clamp
        // implemented" from "clamp absent"; it only asserts the outward behaviour, that clock skew
        // never yields negative growth, by whatever mechanism produces it. The clamp itself is kept
        // in MemoryWatchdog.cs as defense-in-depth (see the comment there) even though it is
        // currently unreachable in its effect.
        var (wd, clock, proc, _) = Build();
        var id = Guid.NewGuid();
        proc.Readings[10] = 2 * Gb;
        wd.OnAccountLaunched(id, pid: 10);
        wd.Sample();

        clock.Advance(TimeSpan.FromHours(-1)); // clock skew
        proc.Readings[10] = 3 * Gb;
        wd.Sample();

        Assert.True(Assert.Single(wd.GetSnapshot().Accounts).GrowthBytesPerHour >= 0);
    }
}
