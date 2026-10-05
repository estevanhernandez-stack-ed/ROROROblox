using System;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Pins the <see cref="MemoryWatchdog.GetSnapshot"/> pre-first-sample contract: callers must
/// never see a null <see cref="MemoryPressureSnapshot.Accounts"/>. This is the regression guard
/// for a bug Task 7 discovered live — <c>MainViewModel</c> is the first real caller of
/// <c>GetSnapshot()</c> that can run before any <see cref="MemoryWatchdog.Sample"/> completes
/// (its own 30s UI ticker starts independently of the watchdog's 30s sample timer), and a bare
/// <c>default(MemoryPressureSnapshot)</c> would have handed it a null list. Fixed at the type's
/// field default rather than patched at each call site, so every future consumer (System Health
/// reporting, etc.) inherits the guarantee for free.
/// </summary>
public class MemoryWatchdogSnapshotTests
{
    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeProcessMemory : IProcessMemoryProbe
    {
        public bool TryReadPrivateBytes(int pid, out long privateBytes)
        {
            privateBytes = 0;
            return false;
        }
    }

    private sealed class FakeSystemMemory : ISystemMemoryProbe
    {
        public bool TryRead(out long total, out long available)
        {
            total = 0;
            available = 0;
            return false;
        }
    }

    [Fact]
    public void GetSnapshot_BeforeAnySample_ReturnsNonNullEmptyAccounts()
    {
        // Fails (NullReferenceException on Assert.Empty, or an outright null) if the `_last`
        // field initializer in MemoryWatchdog is ever simplified back to
        // `private MemoryPressureSnapshot _last;` — the exact regression this test exists to
        // catch. Deliberately does NOT call Start() or Sample() first.
        var wd = new MemoryWatchdog(new FakeProcessMemory(), new FakeSystemMemory(), new FakeClock());

        var snapshot = wd.GetSnapshot();

        Assert.NotNull(snapshot.Accounts);
        Assert.Empty(snapshot.Accounts);
        Assert.False(snapshot.HasProjection);
    }

    private sealed class ReadableProcessMemory : IProcessMemoryProbe
    {
        public long Bytes;
        public bool TryReadPrivateBytes(int pid, out long privateBytes)
        {
            privateBytes = Bytes;
            return true;
        }
    }

    private sealed class ReadableSystemMemory : ISystemMemoryProbe
    {
        public long Total = 32L * 1024 * 1024 * 1024;
        public long Available = 20L * 1024 * 1024 * 1024;
        public bool TryRead(out long total, out long available)
        {
            total = Total;
            available = Available;
            return true;
        }
    }

    /// <summary>
    /// Stopping the watchdog has to retract its last opinion, because nothing will refresh it.
    /// <para>
    /// The badge and the memory chips are cleared by <c>MainViewModel</c>'s 30s ticker evaluating
    /// <see cref="MemoryPressureEvaluator.IsClear"/> against the latest snapshot — a reader that
    /// keeps running after the watchdog stops. So a sampler halted while below reserve would leave
    /// a snapshot that says "in trouble" with nothing left to ever say otherwise, pinning a warning
    /// on screen for the rest of the session. Turning memory watching off is exactly when that
    /// happens (<c>MemoryWatchdogGate</c>, 2026-10-05), which is why the retraction lives in
    /// <see cref="MemoryWatchdog.Stop"/> rather than at one caller.
    /// </para>
    /// </summary>
    [Fact]
    public void Stop_RetractsAWarningSnapshotNothingWillRefresh()
    {
        const long gb = 1024L * 1024 * 1024;
        var proc = new ReadableProcessMemory { Bytes = 2 * gb };
        var sys = new ReadableSystemMemory { Available = 1 * gb };
        var wd = new MemoryWatchdog(proc, sys, new FakeClock()) { ReserveBytes = 8 * gb };
        wd.OnAccountLaunched(Guid.NewGuid(), 10);
        wd.Sample();
        Assert.False(MemoryPressureEvaluator.IsClear(wd.GetSnapshot(), 120),
            "the sampled state really is below reserve, or this test proves nothing.");

        wd.Stop();

        Assert.True(MemoryPressureEvaluator.IsClear(wd.GetSnapshot(), 120),
            "a stopped watchdog holds no opinion; a stale one would pin the badge on forever.");
        Assert.Empty(wd.GetSnapshot().Accounts);
    }
}
