using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Coverage of <see cref="RobloxInstanceStopper"/> — the one-click "stop all running Roblox
/// clients" teardown (multi-instance lane). Enumerates running RobloxPlayerBeta.exe via
/// <see cref="IRobloxRunningProbe"/> and force-closes each through an injected kill seam.
/// Degrade-safe: a single kill failure (e.g. access denied, already-exited) must not abort the
/// rest, and a probe failure resolves to "stopped nothing" rather than throwing.
/// </summary>
public class RobloxInstanceStopperTests
{
    private sealed class FakeProbe : IRobloxRunningProbe
    {
        private readonly IReadOnlyList<int> _pids;
        private readonly Exception? _throw;
        public FakeProbe(params int[] pids) => _pids = pids;
        public FakeProbe(Exception ex) { _pids = Array.Empty<int>(); _throw = ex; }
        public IReadOnlyList<int> GetRunningPlayerPids() => _throw is null ? _pids : throw _throw;

        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers()
            => _throw is null ? _pids.Select(p => new RobloxProcessInfo(p, false)).ToArray() : throw _throw;
    }

    [Fact]
    public void StopAll_StopsEveryRunningPid()
    {
        var killed = new List<int>();
        var stopper = new RobloxInstanceStopper(new FakeProbe(101, 202, 303), killByPid: killed.Add);

        var count = stopper.StopAll();

        Assert.Equal(3, count);
        Assert.Equal(new[] { 101, 202, 303 }, killed);
    }

    [Fact]
    public void StopAll_ReturnsZero_AndDoesNotKill_WhenNoneRunning()
    {
        var killed = new List<int>();
        var stopper = new RobloxInstanceStopper(new FakeProbe(), killByPid: killed.Add);

        var count = stopper.StopAll();

        Assert.Equal(0, count);
        Assert.Empty(killed);
    }

    [Fact]
    public void StopAll_ContinuesAndCountsSurvivors_WhenOneKillThrows()
    {
        var killed = new List<int>();
        Action<int> killer = pid =>
        {
            if (pid == 202) throw new InvalidOperationException("access denied");
            killed.Add(pid);
        };
        var stopper = new RobloxInstanceStopper(new FakeProbe(101, 202, 303), killByPid: killer);

        var count = stopper.StopAll();

        Assert.Equal(2, count);                 // 101 + 303 stopped; 202 failed but did not abort the loop
        Assert.Equal(new[] { 101, 303 }, killed);
    }

    [Fact]
    public void StopAll_NeverThrows_WhenProbeThrows()
    {
        var stopper = new RobloxInstanceStopper(new FakeProbe(new IOException("scan failed")), killByPid: _ => { });

        var count = stopper.StopAll();

        Assert.Equal(0, count);
    }
}
