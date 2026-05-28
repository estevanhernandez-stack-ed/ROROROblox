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
    private readonly IRobloxRunningProbe _probe;
    private readonly Action<int> _killByPid;
    private readonly ILogger<RobloxInstanceStopper> _log;

    public RobloxInstanceStopper(
        IRobloxRunningProbe probe,
        Action<int>? killByPid = null,
        ILogger<RobloxInstanceStopper>? log = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _killByPid = killByPid ?? KillByPid;
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

        var stopped = 0;
        foreach (var pid in pids)
        {
            try
            {
                _killByPid(pid);
                stopped++;
            }
            catch (Exception ex)
            {
                // Degrade-safe: a single kill failure (access denied, already-exited) must not
                // abort the teardown of the remaining clients.
                _log.LogWarning(ex, "StopAll: kill failed for pid {Pid}; continuing.", pid);
            }
        }

        if (pids.Count > 0)
        {
            _log.LogInformation("StopAll: stopped {Stopped}/{Total} Roblox instance(s).", stopped, pids.Count);
        }
        return stopped;
    }

    private static void KillByPid(int pid)
    {
        using var process = Process.GetProcessById(pid);
        process.Kill(entireProcessTree: false);
    }
}
