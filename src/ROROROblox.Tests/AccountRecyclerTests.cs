using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class AccountRecyclerTests
{
    private sealed class FakeStopper : IRobloxInstanceStopper
    {
        public readonly List<Guid> Stopped = new();
        public int StopAll() => 0;
        public bool StopAccount(Guid accountId) { Stopped.Add(accountId); return true; }
        public int StopWindowless() => 0;
    }

    private sealed class RecordingLauncher
    {
        public readonly List<(Guid Id, LaunchTarget Target)> Launches = new();
        public int NextPid = 4242;
        public Task<int> LaunchAsync(Guid id, LaunchTarget target, CancellationToken ct = default)
        {
            Launches.Add((id, target));
            return Task.FromResult(NextPid);
        }
    }

    private sealed class SpyWatchdog : IMemoryWatchdog
    {
        public readonly List<(Guid Id, int Pid)> Resets = new();
        public long CapBytes { get; set; }
        public long ReserveBytes { get; set; }
        public int ProjectionWarnMinutes { get; set; }
        public event EventHandler<MemoryPressureSnapshot>? PressureCrossed { add { } remove { } }
        public void OnAccountLaunched(Guid accountId, int pid) { }
        public void OnAccountExited(Guid accountId, int pid) { }
        public void ResetBaseline(Guid accountId, int pid) => Resets.Add((accountId, pid));
        public void Start() { }
        public void Stop() { }
        public void Sample() { }
        public MemoryPressureSnapshot GetSnapshot() => new(0, 0, 0, false, null, []);
    }

    [Fact]
    public async Task Recycle_StopsThenRelaunchesToTheSameLaunchTarget()
    {
        var id = Guid.NewGuid();
        var target = new LaunchTarget.Place(PlaceId: 8737899170);
        var stopper = new FakeStopper();
        var launcher = new RecordingLauncher();
        var watchdog = new SpyWatchdog();
        var recycler = new AccountRecycler(stopper, launcher.LaunchAsync, watchdog);

        var ok = await recycler.RecycleAsync(id, target);

        Assert.True(ok);
        Assert.Equal(id, Assert.Single(stopper.Stopped));
        var launch = Assert.Single(launcher.Launches);
        Assert.Equal(id, launch.Id);
        Assert.Same(target, launch.Target); // the SAME target — you land where you were
    }

    [Fact]
    public async Task Recycle_ResetsTheWatchdogBaselineToTheNewPid()
    {
        var id = Guid.NewGuid();
        var watchdog = new SpyWatchdog();
        var launcher = new RecordingLauncher { NextPid = 777 };
        var recycler = new AccountRecycler(new FakeStopper(), launcher.LaunchAsync, watchdog);

        await recycler.RecycleAsync(id, new LaunchTarget.Home());

        Assert.Equal((id, 777), Assert.Single(watchdog.Resets));
    }

    [Fact]
    public async Task Recycle_RelaunchFails_ReportsFailureAndDoesNotResetBaseline()
    {
        var id = Guid.NewGuid();
        var watchdog = new SpyWatchdog();
        var recycler = new AccountRecycler(
            new FakeStopper(),
            (_, _, _) => Task.FromResult(0), // 0 = launch produced no process
            watchdog);

        var ok = await recycler.RecycleAsync(id, new LaunchTarget.Home());

        Assert.False(ok);
        Assert.Empty(watchdog.Resets); // a stale baseline is worse than none
    }

    [Fact]
    public async Task Recycle_DoesNotLogThePrivateServerCode()
    {
        // CRITICAL 2 (final-branch review, 2026-08-01): the old `{Target}` log parameter rendered
        // via Serilog's default (non-@) ToString() path, and LaunchTarget.PrivateServer is a
        // positional record carrying a joinable-server Code -- a credential -- so the code landed
        // verbatim in rororoblox-.log, a file DiagnosticsWindow packs into user-shared support
        // bundles. Discriminator: assert the sentinel code never appears in ANY captured log line.
        const string sentinelCode = "SENTINEL-JOINABLE-CODE-DO-NOT-LOG-9f2a";
        var id = Guid.NewGuid();
        var target = new LaunchTarget.PrivateServer(PlaceId: 8737899170, Code: sentinelCode, Kind: PrivateServerCodeKind.LinkCode);
        var log = new CapturingLogger<AccountRecycler>();
        var recycler = new AccountRecycler(new FakeStopper(), new RecordingLauncher().LaunchAsync, new SpyWatchdog(), log);

        await recycler.RecycleAsync(id, target);

        Assert.DoesNotContain(log.Snapshot(), line => line.Contains(sentinelCode));
    }
}
