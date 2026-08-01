using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Task 9: <see cref="DiagnosticsCollector"/> gains RAM + per-account memory reporting. Every new
/// probe read is best-effort — a probe failure becomes a zero/empty field, never an exception —
/// mirroring the class's existing "clean snapshot always produceable" contract for Roblox/WebView2
/// detection.
/// </summary>
public class DiagnosticsCollectorTests
{
    private static DiagnosticsCollector BuildCollector(
        ISystemMemoryProbe? systemMemoryProbe = null,
        IMemoryWatchdog? memoryWatchdog = null)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rororo-diag-test-{Guid.NewGuid():N}.dat");
        return new DiagnosticsCollector(
            new AccountStore(path),
            new FakeRobloxProcessTracker(),
            new FakeMutexHolder(),
            logDirectory: string.Empty,
            dataDirectory: string.Empty,
            systemMemoryProbe: systemMemoryProbe ?? new FakeSystemMemoryProbe(),
            memoryWatchdog: memoryWatchdog ?? new FakeMemoryWatchdog());
    }

    [Fact]
    public async Task Collect_ReportsInstalledAndAvailableRam()
    {
        var collector = BuildCollector(new FakeSystemMemoryProbe());

        var snap = await collector.CollectAsync();

        Assert.Equal(34_359_738_368, snap.TotalPhysicalMemoryBytes);
        Assert.Equal(21_474_836_480, snap.AvailablePhysicalMemoryBytes);
    }

    [Fact]
    public async Task Collect_ProbeFails_ReportsZeroRatherThanAGuess()
    {
        // Bogus non-zero out-params on the failure path: if the collector ever stopped gating on
        // TryRead's bool return and just copied whatever the out-params held, this test would
        // catch it (999999/888888 leaking through) instead of silently passing on a fake that
        // happens to zero its own out-params on failure too.
        var collector = BuildCollector(new FakeSystemMemoryProbe { Ok = false, Total = 999_999, Available = 888_888 });

        var snap = await collector.CollectAsync();

        // The class contract is "a missing piece becomes 'not detected' rather than throwing."
        // Zero is honest here; a fabricated plausible number in a support bundle is not.
        Assert.Equal(0, snap.TotalPhysicalMemoryBytes);
        Assert.Equal(0, snap.AvailablePhysicalMemoryBytes);
    }

    [Fact]
    public async Task Collect_ReportsPerAccountMemoryFromWatchdogSnapshot()
    {
        var accountId = Guid.NewGuid();
        var watchdogSnapshot = new MemoryPressureSnapshot(
            AvailableBytes: 21_474_836_480,
            AggregateGrowthBytesPerHour: 104_857_600,
            MinutesToCeiling: 240,
            HasProjection: true,
            TargetAccountId: accountId,
            Accounts: new[]
            {
                new AccountMemory(accountId, 2_147_483_648, 104_857_600, 240, false, true, ReadOk: true),
            });
        var collector = BuildCollector(memoryWatchdog: new FakeMemoryWatchdog { Snapshot = watchdogSnapshot });

        var snap = await collector.CollectAsync();

        var only = Assert.Single(snap.AccountMemory);
        Assert.Equal(accountId, only.AccountId);
        Assert.Equal(2_147_483_648, only.PrivateBytes);
        Assert.True(only.ReadOk);
    }

    [Fact]
    public async Task Collect_WatchdogGetSnapshotThrows_StillReturnsCompleteSnapshotWithEmptyAccountMemory()
    {
        // Pins the class's stated promise -- "a clean snapshot is always produceable even on a
        // busted machine" -- specifically for the new watchdog read. A busted IMemoryWatchdog
        // (GetSnapshot throwing, not just returning an empty result) must not blow up the whole
        // collector: AccountMemory degrades to empty-not-null and every other field still comes
        // back populated, exactly like the existing account-store/registry probes above it.
        var collector = BuildCollector(memoryWatchdog: new FakeMemoryWatchdog { ThrowOnGetSnapshot = true });

        var snap = await collector.CollectAsync();

        Assert.NotNull(snap.AccountMemory);
        Assert.Empty(snap.AccountMemory);
        // Not just "didn't throw" -- prove the rest of the snapshot is still real, not a
        // half-built object abandoned mid-construction by a caught exception.
        Assert.Equal(34_359_738_368, snap.TotalPhysicalMemoryBytes);
    }

    // ---- fakes ----

    private sealed class FakeSystemMemoryProbe : ISystemMemoryProbe
    {
        public bool Ok = true;
        public long Total = 34_359_738_368;  // 32 GB
        public long Available = 21_474_836_480;  // 20 GB

        // Out-params are copied unconditionally, even when Ok is false, so a test can set bogus
        // non-zero values on the failure path to prove the collector actually gates on the
        // returned bool rather than trusting whatever the out-params contain.
        public bool TryRead(out long total, out long available)
        {
            total = Total;
            available = Available;
            return Ok;
        }
    }

    private sealed class FakeMemoryWatchdog : IMemoryWatchdog
    {
        public MemoryPressureSnapshot Snapshot = new(0, 0, 0, false, null, []);
        public bool ThrowOnGetSnapshot;

        public long CapBytes { get; set; }
        public long ReserveBytes { get; set; }
        public int ProjectionWarnMinutes { get; set; }

        public event EventHandler<MemoryPressureSnapshot>? PressureCrossed { add { } remove { } }

        public void OnAccountLaunched(Guid accountId, int pid) => throw new NotImplementedException();
        public void OnAccountExited(Guid accountId) => throw new NotImplementedException();
        public void ResetBaseline(Guid accountId, int pid) => throw new NotImplementedException();
        public void Start() => throw new NotImplementedException();
        public void Stop() => throw new NotImplementedException();
        public void Sample() => throw new NotImplementedException();

        public MemoryPressureSnapshot GetSnapshot() =>
            ThrowOnGetSnapshot ? throw new InvalidOperationException("watchdog is busted") : Snapshot;
    }

    private sealed class FakeRobloxProcessTracker : IRobloxProcessTracker
    {
        public IReadOnlyDictionary<Guid, TrackedProcess> Attached { get; } = new Dictionary<Guid, TrackedProcess>();

        public Task TrackLaunchAsync(Guid accountId, DateTimeOffset launchedAtUtc, CancellationToken ct = default) => throw new NotImplementedException();
        public bool AttachExisting(Guid accountId, int pid) => throw new NotImplementedException();
        public bool IsTracking(Guid accountId) => throw new NotImplementedException();
        public bool RequestClose(Guid accountId) => throw new NotImplementedException();
        public bool Kill(Guid accountId) => throw new NotImplementedException();
        public event EventHandler<RobloxProcessEventArgs>? ProcessAttached { add { } remove { } }
        public event EventHandler<RobloxProcessEventArgs>? ProcessAttachFailed { add { } remove { } }
        public event EventHandler<RobloxProcessEventArgs>? ProcessExited { add { } remove { } }
    }

    private sealed class FakeMutexHolder : IMutexHolder
    {
        public string MutexName => @"Local\fake";
        public bool IsHeld => false;
        public bool Acquire() => throw new NotImplementedException();
        public void Release() => throw new NotImplementedException();
        public bool IsHeldElsewhere() => throw new NotImplementedException();
        public event EventHandler? MutexLost { add { } remove { } }
    }
}
