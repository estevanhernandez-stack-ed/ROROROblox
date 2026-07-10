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
    /// Acquire-first gate. The caller attempts the singleton name first and passes the outcome.
    ///
    /// <list type="bullet">
    ///   <item>Roblox holds it (its Event) → <see cref="StartupGateResult.Blocked"/>. Multi-instance
    ///   is genuinely unavailable; offer recovery.</item>
    ///   <item>A compatible tool holds it (a Mutex) → <see cref="StartupGateResult.SharedLock"/>.
    ///   Roblox lost its own singleton, so multi-instance works. Do NOT block.</item>
    ///   <item>Acquired, no leftovers → Clean. Acquired with leftovers → Leftover(split).</item>
    /// </list>
    ///
    /// Fails open to Clean if the process scan throws (we hold the name, so proceeding is safe).
    /// </summary>
    public StartupGateResult Evaluate(MutexAcquireOutcome outcome)
    {
        switch (outcome)
        {
            case MutexAcquireOutcome.HeldByRoblox:
                _log.LogInformation(
                    "StartupGate: singleton name held by Roblox (as an event); multi-instance unavailable. Blocking.");
                return new StartupGateResult.Blocked();

            case MutexAcquireOutcome.HeldByCompatibleTool:
                // Held as a MUTEX, so its creator squats the name the way we do. Roblox therefore
                // lost its own singleton and multi-instance still works — nothing to recover from.
                _log.LogInformation(
                    "StartupGate: singleton name held by another RoRoRo or compatible tool (a mutex, not Roblox's event); multi-instance still works. Proceeding without the handle.");
                return new StartupGateResult.SharedLock();

            case MutexAcquireOutcome.Failed:
                _log.LogWarning(
                    "StartupGate: CreateMutex failed for an unrecognized reason; blocking (conservative).");
                return new StartupGateResult.Blocked();
        }

        try
        {
            var players = _probe.GetRunningPlayers();
            if (players.Count == 0)
            {
                _log.LogInformation("StartupGate: singleton name acquired, no leftover Roblox processes; clean start.");
                return new StartupGateResult.Clean();
            }

            var windowed = players.Count(p => p.HasWindow);
            var windowless = players.Count - windowed;
            _log.LogInformation(
                "StartupGate: singleton name acquired with {Windowless} windowless + {Windowed} windowed leftover Roblox process(es); informational.",
                windowless, windowed);
            return new StartupGateResult.Leftover(windowless, windowed);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "StartupGate: process scan threw after acquiring the name; proceeding (we hold it).");
            return new StartupGateResult.Clean();
        }
    }

    /// <summary>Back-compat shim for callers that only know acquired/not.</summary>
    public StartupGateResult Evaluate(bool mutexAcquired)
        => Evaluate(mutexAcquired ? MutexAcquireOutcome.Acquired : MutexAcquireOutcome.HeldByRoblox);
}
