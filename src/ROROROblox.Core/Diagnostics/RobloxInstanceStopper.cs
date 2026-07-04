using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Default <see cref="IRobloxInstanceStopper"/> — enumerates running RobloxPlayerBeta.exe via
/// <see cref="IRobloxRunningProbe"/> and force-closes each by PID. The kill is an injected seam so
/// tests drive the loop without real processes. Degrade-safe throughout: a probe failure stops
/// nothing (returns 0) and a single kill failure never aborts the remaining teardown.
/// </summary>
public sealed class RobloxInstanceStopper : IRobloxInstanceStopper
{
    /// <summary>
    /// Total budget for the post-kill exit wait, shared across all killed processes. Typical
    /// RobloxPlayerBeta teardown after Kill() is well under a second; the budget only binds
    /// when a process wedges on exit.
    /// </summary>
    private const int ExitWaitBudgetMs = 3000;

    private readonly IRobloxRunningProbe _probe;
    private readonly Action<int> _killByPid;
    private readonly Func<int, TimeSpan, bool> _waitForExitByPid;
    private readonly int _exitWaitBudgetMs;
    private readonly ILogger<RobloxInstanceStopper> _log;

    public RobloxInstanceStopper(
        IRobloxRunningProbe probe,
        Action<int>? killByPid = null,
        ILogger<RobloxInstanceStopper>? log = null,
        Func<int, TimeSpan, bool>? waitForExitByPid = null,
        int? exitWaitBudgetMs = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _killByPid = killByPid ?? KillByPid;
        _waitForExitByPid = waitForExitByPid ?? WaitForExitByPid;
        _exitWaitBudgetMs = exitWaitBudgetMs ?? ExitWaitBudgetMs;
        _log = log ?? NullLogger<RobloxInstanceStopper>.Instance;
    }

    public int StopAll()
    {
        IReadOnlyList<int> pids;
        try
        {
            pids = _probe.GetRunningPlayerPids();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "StopAll: running-process scan failed; stopped nothing.");
            return 0;
        }

        var killed = 0;
        foreach (var pid in pids)
        {
            try
            {
                _killByPid(pid);
                killed++;
            }
            catch (Exception ex)
            {
                // Degrade-safe: a single kill failure must not abort the teardown of the
                // remaining clients. Crucially this is NOT the same as "don't wait for it" —
                // the most common throw is ERROR_ACCESS_DENIED on a process that is ALREADY
                // terminating (Kill()'s already-exited short-circuit only fires once the exit
                // status is set, which happens after teardown begins). That pid is dying, still
                // holds the singleton mutex object, and MUST be waited on below — so the wait
                // set is every probed pid, not just the cleanly-killed ones.
                _log.LogWarning(ex, "StopAll: kill failed for pid {Pid}; continuing (will still wait for exit).", pid);
            }
        }

        // Kill() returns at signal time, not teardown time — and the Roblox singleton mutex
        // object survives until the dying process's handle table is torn down. Callers
        // (BLOCKED modal's Close-for-me, LEFTOVER clean-up, tray Stop-all) re-acquire the
        // mutex immediately after StopAll returns, so wait (bounded) for the actual exits;
        // without this the immediate re-acquire races the teardown and reports "Roblox still
        // running" for clients that are already dead — they just haven't finished dying
        // (user report 2026-07-03). Wait on EVERY probed pid: WaitForExitByPid returns true
        // instantly for an already-gone pid, so the only cost of over-waiting is the bounded
        // budget spent on a genuinely stuck (unkillable-but-live) process, which then reports
        // truthfully rather than racing the caller's re-acquire.
        if (pids.Count > 0)
        {
            var deadline = Environment.TickCount64 + _exitWaitBudgetMs;
            var stillExiting = 0;
            foreach (var pid in pids)
            {
                var remaining = TimeSpan.FromMilliseconds(Math.Max(0, deadline - Environment.TickCount64));
                if (!_waitForExitByPid(pid, remaining))
                {
                    stillExiting++;
                }
            }
            if (stillExiting > 0)
            {
                _log.LogWarning(
                    "StopAll: {Count} process(es) had not finished exiting within the {Budget} ms wait budget.",
                    stillExiting, _exitWaitBudgetMs);
            }
            _log.LogInformation("StopAll: signalled {Stopped}/{Total} Roblox instance(s); all probed pids waited for exit.", killed, pids.Count);
        }
        return killed;
    }

    private static void KillByPid(int pid)
    {
        using var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: false);
    }

    private static bool WaitForExitByPid(int pid, TimeSpan timeout)
    {
        // Re-resolves by pid rather than holding the Process handle from kill time. Pid reuse
        // in the sub-millisecond gap between the kill loop and this wait is negligible (Windows
        // does not recycle pids that aggressively) and the worst case is bounded — a rebind to
        // an unrelated live process burns at most the remaining budget and reports truthfully,
        // never corrupts. Holding handles would need the kill/wait seam to carry Process
        // objects, which the injected-seam test design deliberately avoids.
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.WaitForExit((int)Math.Max(0, timeout.TotalMilliseconds));
        }
        catch (ArgumentException)
        {
            return true; // no such process — already fully gone, which is the outcome we want
        }
        catch (InvalidOperationException)
        {
            return true; // exited between GetProcessById and WaitForExit
        }
    }
}
