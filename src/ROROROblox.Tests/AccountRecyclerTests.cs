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
        public void OnAccountExited(Guid accountId) { }
        public void ResetBaseline(Guid accountId, int pid) => Resets.Add((accountId, pid));
        public void Start() { }
        public void Stop() { }
        public void Sample() { }
        public MemoryPressureSnapshot GetSnapshot() => default;
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
}
