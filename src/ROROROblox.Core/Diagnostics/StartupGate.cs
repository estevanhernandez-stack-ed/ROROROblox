using System.Linq;
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

    /// <summary>
    /// Acquire-first gate. Caller acquires the mutex first and passes the result. Not acquired →
    /// Blocked. Acquired + no leftover processes → Clean. Acquired + leftovers → Leftover(split).
    /// Fail-open to Clean if the process scan throws (we hold the lock, so proceeding is safe).
    /// </summary>
    public StartupGateResult Evaluate(bool mutexAcquired)
    {
        if (!mutexAcquired)
        {
            _log.LogInformation("StartupGate: mutex not acquired — Roblox holds the lock; blocking.");
            return new StartupGateResult.Blocked();
        }

        try
        {
            var players = _probe.GetRunningPlayers();
            if (players.Count == 0)
            {
                _log.LogInformation("StartupGate: mutex acquired, no leftover Roblox processes; clean start.");
                return new StartupGateResult.Clean();
            }

            var windowed = players.Count(p => p.HasWindow);
            var windowless = players.Count - windowed;
            _log.LogInformation(
                "StartupGate: mutex acquired with {Windowless} windowless + {Windowed} windowed leftover Roblox process(es); informational.",
                windowless, windowed);
            return new StartupGateResult.Leftover(windowless, windowed);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "StartupGate: process scan threw after mutex acquired; proceeding (we hold the lock).");
            return new StartupGateResult.Clean();
        }
    }
}
