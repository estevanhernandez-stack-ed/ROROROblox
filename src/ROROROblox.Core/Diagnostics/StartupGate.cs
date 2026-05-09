using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Probe-driven shutdown decision for app startup. Wraps <see cref="IRobloxRunningProbe"/>
/// with the soft-fail discipline from spec §5: a false-positive blocks the user from starting
/// the app at all (unrecoverable), a false-negative is recoverable via the existing manual
/// workaround. We bias toward false-negatives by failing open on any probe exception.
/// Cycle 4 (2026-05-08).
/// </summary>
public sealed class StartupGate
{
    private readonly IRobloxRunningProbe _probe;
    private readonly ILogger<StartupGate> _log;

    public StartupGate(IRobloxRunningProbe probe, ILogger<StartupGate>? log = null)
    {
        _probe = probe;
        _log = log ?? NullLogger<StartupGate>.Instance;
    }

    /// <summary>
    /// Returns <c>true</c> if RoRoRo should proceed with normal startup (no foreign Roblox
    /// detected). Returns <c>false</c> if the caller should show the already-running modal
    /// and shut down. On probe failure, returns <c>true</c> (fail-open) and logs a warning.
    /// </summary>
    public bool ShouldProceed()
    {
        try
        {
            var pids = _probe.GetRunningPlayerPids();
            if (pids.Count > 0)
            {
                _log.LogInformation(
                    "StartupGate: detected {Count} running RobloxPlayerBeta.exe process(es) at startup; blocking. PIDs: {Pids}",
                    pids.Count,
                    string.Join(",", pids));
                return false;
            }
            _log.LogInformation("StartupGate: no RobloxPlayerBeta.exe detected; proceeding to mutex.Acquire.");
            return true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "RobloxRunningProbe threw; failing open and proceeding with startup.");
            return true;
        }
    }
}
