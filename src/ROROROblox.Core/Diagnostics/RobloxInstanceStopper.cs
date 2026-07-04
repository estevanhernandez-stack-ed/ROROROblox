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
    private readonly ILogger<RobloxInstanceStopper> _log;

    public RobloxInstanceStopper(
        IRobloxRunningProbe probe,
        Action<int>? killByPid = null,
        ILogger<RobloxInstanceStopper>? log = null,
        Func<int, TimeSpan, bool>? waitForExitByPid = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _killByPid = killByPid ?? KillByPid;
        _waitForExitByPid = waitForExitByPid ?? WaitForExitByPid;
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

        var killedPids = new List<int>(pids.Count);
        foreach (var pid in pids)
        {
            try
            {
                _killByPid(pid);
                killedPids.Add(pid);
            }
            catch (Exception ex)
            {
                // Degrade-safe: a single kill failure (access denied, already-exited) must not
                // abort the teardown of the remaining clients.
                _log.LogWarning(ex, "StopAll: kill failed for pid {Pid}; continuing.", pid);
            }
        }

        // Kill() returns at signal time, not teardown time — and the Roblox singleton mutex
        // object survives until the dying process's handle table is torn down. Callers
        // (BLOCKED modal's Close-for-me, LEFTOVER clean-up, tray Stop-all) re-acquire the
        // mutex immediately after StopAll returns, so wait (bounded) for the actual exits;
        // without this the immediate re-acquire races the teardown and reports "Roblox still
        // running" for clients that are already dead — they just haven't finished dying
        // (user report 2026-07-03).
        if (killedPids.Count > 0)
        {
            var deadline = Environment.TickCount64 + ExitWaitBudgetMs;
            var stillExiting = 0;
            foreach (var pid in killedPids)
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
                    stillExiting, ExitWaitBudgetMs);
            }
        }

        if (pids.Count > 0)
        {
            _log.LogInformation("StopAll: stopped {Stopped}/{Total} Roblox instance(s).", killedPids.Count, pids.Count);
        }
        return killedPids.Count;
    }

    private static void KillByPid(int pid)
    {
        using var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: false);
    }

    private static bool WaitForExitByPid(int pid, TimeSpan timeout)
    {
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
