using System.Diagnostics;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Coverage of <see cref="RobloxInstanceStopper"/> — the one-click "stop all running Roblox
/// clients" teardown (multi-instance lane). Enumerates running RobloxPlayerBeta.exe via
/// <see cref="IRobloxRunningProbe"/> and force-closes each through an injected kill seam.
/// Degrade-safe: a single kill failure (e.g. access denied, already-exited) must not abort the
/// rest, and a probe failure resolves to "stopped nothing" rather than throwing.
/// StopAll's contract is RETURNS-AFTER-EXIT: callers re-acquire the Roblox singleton mutex the
/// moment it returns, so the post-kill wait phase is load-bearing (close-for-me false
/// "still running", 2026-07-03). Fake tests inject a no-op wait; the real-process test pins the
/// actual exit contract end-to-end.
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
        var stopper = new RobloxInstanceStopper(
            new FakeProbe(101, 202, 303), killByPid: killed.Add, waitForExitByPid: (_, _) => true);

        var count = stopper.StopAll();

        Assert.Equal(3, count);
        Assert.Equal(new[] { 101, 202, 303 }, killed);
    }

    [Fact]
    public void StopAll_ReturnsZero_AndDoesNotKill_WhenNoneRunning()
    {
        var killed = new List<int>();
        var stopper = new RobloxInstanceStopper(
            new FakeProbe(), killByPid: killed.Add, waitForExitByPid: (_, _) => true);

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
        var stopper = new RobloxInstanceStopper(
            new FakeProbe(101, 202, 303), killByPid: killer, waitForExitByPid: (_, _) => true);

        var count = stopper.StopAll();

        Assert.Equal(2, count);                 // 101 + 303 stopped; 202 failed but did not abort the loop
        Assert.Equal(new[] { 101, 303 }, killed);
    }

    [Fact]
    public void StopAll_NeverThrows_WhenProbeThrows()
    {
        var stopper = new RobloxInstanceStopper(
            new FakeProbe(new IOException("scan failed")), killByPid: _ => { }, waitForExitByPid: (_, _) => true);

        var count = stopper.StopAll();

        Assert.Equal(0, count);
    }

    [Fact]
    public void StopAll_WaitsForExit_OfEveryKilledPid()
    {
        var waited = new List<int>();
        var stopper = new RobloxInstanceStopper(
            new FakeProbe(101, 202, 303),
            killByPid: _ => { },
            waitForExitByPid: (pid, _) => { waited.Add(pid); return true; });

        stopper.StopAll();

        Assert.Equal(new[] { 101, 202, 303 }, waited);
    }

    [Fact]
    public void StopAll_DoesNotWaitForPids_WhoseKillFailed()
    {
        var waited = new List<int>();
        Action<int> killer = pid =>
        {
            if (pid == 202) throw new InvalidOperationException("access denied");
        };
        var stopper = new RobloxInstanceStopper(
            new FakeProbe(101, 202, 303),
            killByPid: killer,
            waitForExitByPid: (pid, _) => { waited.Add(pid); return true; });

        stopper.StopAll();

        // 202's kill failed — waiting on it would burn budget on a process we never signalled.
        Assert.Equal(new[] { 101, 303 }, waited);
    }

    [Fact]
    public void StopAll_NeverThrows_WhenWaitReportsStillRunning()
    {
        var stopper = new RobloxInstanceStopper(
            new FakeProbe(101), killByPid: _ => { }, waitForExitByPid: (_, _) => false);

        var count = stopper.StopAll();

        Assert.Equal(1, count); // wedge on exit is logged, not thrown — the kill itself succeeded
    }

    [Fact]
    public void StopAll_ReturnsOnlyAfterRealProcessExit()
    {
        // Real-process pin of the returns-after-exit contract (same pattern as the
        // DefaultPluginProcessStarter real-process tests): spawn a long-running ping.exe
        // (present on every Windows box/CI image), stop it through the REAL kill + wait
        // seams, and assert it has fully exited the moment StopAll returns — the exact
        // property the mutex re-acquire in TryRecoverMultiInstance depends on.
        var psi = new ProcessStartInfo
        {
            FileName = "ping.exe",
            Arguments = "-t 127.0.0.1",
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        try
        {
            var stopper = new RobloxInstanceStopper(new FakeProbe(process.Id));

            var count = stopper.StopAll();

            Assert.Equal(1, count);
            Assert.True(process.HasExited, "StopAll returned before the killed process finished exiting.");
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(); } catch { /* best-effort cleanup */ }
            }
        }
    }
}
