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
    public void StopAll_WaitsForExit_OfEveryProbedPid()
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
    public void StopAll_WaitsForPid_EvenWhenItsKillThrew()
    {
        // The motivating race: a client already mid-teardown makes Kill() throw
        // ERROR_ACCESS_DENIED (it's already terminating), yet it still holds the singleton
        // mutex object until teardown completes — so StopAll MUST wait for it, or the caller's
        // immediate mutex re-acquire races the teardown and reports "still running" (the
        // 2026-07-03 bug). Dropping the throwing pid from the wait set reintroduces the bug.
        var waited = new List<int>();
        Action<int> killer = pid =>
        {
            if (pid == 202) throw new InvalidOperationException("access denied — process is terminating");
        };
        var stopper = new RobloxInstanceStopper(
            new FakeProbe(101, 202, 303),
            killByPid: killer,
            waitForExitByPid: (pid, _) => { waited.Add(pid); return true; });

        stopper.StopAll();

        Assert.Contains(202, waited);
        Assert.Equal(new[] { 101, 202, 303 }, waited);
    }

    [Fact]
    public void StopAll_SharesTheExitWaitBudget_AcrossPids()
    {
        // The budget is a single deadline shared across all pids, not a fresh per-pid timeout —
        // a wedged first process must not let a slow second one wait the full budget again.
        // Inject a small budget + a first-pid stall so the second pid's remaining is provably
        // less than the budget.
        var remainingByPid = new Dictionary<int, TimeSpan>();
        var stopper = new RobloxInstanceStopper(
            new FakeProbe(101, 202),
            killByPid: _ => { },
            waitForExitByPid: (pid, remaining) =>
            {
                remainingByPid[pid] = remaining;
                if (pid == 101) Thread.Sleep(120); // burn part of the shared budget
                return false;                       // report "still exiting" so the budget keeps draining
            },
            exitWaitBudgetMs: 200);

        stopper.StopAll();

        // First pid sees ~the full budget; never MORE than the budget (proves it's not a fresh
        // per-pid timeout starting above 200 ms).
        Assert.True(
            remainingByPid[101] <= TimeSpan.FromMilliseconds(200) && remainingByPid[101] >= TimeSpan.FromMilliseconds(140),
            $"first pid remaining was {remainingByPid[101].TotalMilliseconds} ms; expected ~200 ms (<= budget).");
        Assert.True(
            remainingByPid[202] < remainingByPid[101],
            $"second pid remaining ({remainingByPid[202].TotalMilliseconds} ms) should be less than the first's ({remainingByPid[101].TotalMilliseconds} ms) — budget is shared.");
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
    public void StopAll_RealProcess_KillAndWaitPathRunsEndToEnd()
    {
        // Integration smoke for the DEFAULT (uninjected) kill + wait seams: spawn a
        // long-running ping.exe (present on every Windows box/CI image) and confirm StopAll
        // runs the real Process.Kill + Process.WaitForExit path end-to-end without throwing,
        // leaving the process gone. NOTE: this does NOT by itself pin the returns-AFTER-exit
        // ordering — ping dies within a millisecond of Kill(), so HasExited would read true
        // even if the wait were skipped. The wait LOGIC (which pids, shared budget, throw-path
        // inclusion) is pinned by the fake-seam tests above; this covers the default wrappers.
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
