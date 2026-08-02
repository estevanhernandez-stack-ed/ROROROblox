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
    private readonly IRobloxProcessTracker? _tracker;
    private readonly Action<int> _killByPid;
    private readonly Func<int, TimeSpan, bool> _waitForExitByPid;
    private readonly int _exitWaitBudgetMs;
    private readonly ILogger<RobloxInstanceStopper> _log;

    public RobloxInstanceStopper(
        IRobloxRunningProbe probe,
        IRobloxProcessTracker? tracker = null,
        Action<int>? killByPid = null,
        ILogger<RobloxInstanceStopper>? log = null,
        Func<int, TimeSpan, bool>? waitForExitByPid = null,
        int? exitWaitBudgetMs = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        // Optional: StopAll (the probe-driven "kill everything" lane) never needed a pid->account
        // map. StopAccount (Task 8's Recycle) does, so it resolves through this DI-registered
        // singleton when present; a caller that never invokes StopAccount (e.g. the existing
        // fake-seam StopAll tests) doesn't need to supply one.
        _tracker = tracker;
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

        return KillAndWaitAll(pids, "StopAll");
    }

    public int StopWindowless()
    {
        IReadOnlyList<RobloxProcessInfo> players;
        try
        {
            players = _probe.GetRunningPlayers();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "StopWindowless: running-process scan failed; stopped nothing.");
            return 0;
        }

        // The safety-critical filter. RobloxRunningProbe.ReadHasWindow fails CLOSED — a process
        // it could not classify comes back HasWindow == true — so restricting to !HasWindow here
        // is the entire justification for the LEFTOVER modal's "Clear strays" button skipping a
        // confirmation prompt. Never loosen this to anything but an explicit HasWindow == false.
        var pids = players.Where(p => !p.HasWindow).Select(p => p.Pid).ToArray();

        return KillAndWaitAll(pids, "StopWindowless");
    }

    /// <summary>
    /// Shared kill-and-wait teardown for <see cref="StopAll"/> and <see cref="StopWindowless"/>:
    /// best-effort kill of every pid (one failure doesn't abort the rest), then a bounded wait
    /// for exit across all of them sharing a single deadline. <paramref name="label"/> only
    /// affects the log lines so each caller's Information/Warning output stays attributable.
    /// </summary>
    private int KillAndWaitAll(IReadOnlyList<int> pids, string label)
    {
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
                _log.LogWarning(ex, "{Label}: kill failed for pid {Pid}; continuing (will still wait for exit).", label, pid);
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
                    "{Label}: {Count} process(es) had not finished exiting within the {Budget} ms wait budget.",
                    label, stillExiting, _exitWaitBudgetMs);
            }
            _log.LogInformation("{Label}: signalled {Stopped}/{Total} Roblox instance(s); all probed pids waited for exit.", label, killed, pids.Count);
        }
        return killed;
    }

    public bool StopAccount(Guid accountId)
    {
        if (_tracker is null)
        {
            _log.LogWarning("StopAccount: no process tracker wired; cannot resolve a pid for account {AccountId}.", accountId);
            return false;
        }

        if (!_tracker.Attached.TryGetValue(accountId, out var tracked))
        {
            _log.LogInformation("StopAccount: account {AccountId} has no tracked process; nothing to stop.", accountId);
            return false;
        }

        var pid = tracked.Pid;
        try
        {
            _killByPid(pid);
        }
        catch (Exception ex)
        {
            // Same race StopAll degrades: ERROR_ACCESS_DENIED on a process already mid-teardown
            // is expected, not fatal — it still holds the mutex object until it finishes dying,
            // so the wait below runs regardless of whether the kill call itself succeeded.
            _log.LogWarning(ex, "StopAccount: kill failed for account {AccountId} pid {Pid}; still waiting for exit.", accountId, pid);
        }

        if (!_waitForExitByPid(pid, TimeSpan.FromMilliseconds(_exitWaitBudgetMs)))
        {
            _log.LogWarning(
                "StopAccount: account {AccountId} pid {Pid} had not finished exiting within the {Budget} ms wait budget.",
                accountId, pid, _exitWaitBudgetMs);
        }

        _log.LogInformation("StopAccount: signalled account {AccountId} pid {Pid}.", accountId, pid);
        return true;
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
