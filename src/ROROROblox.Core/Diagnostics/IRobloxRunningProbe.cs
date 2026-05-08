namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Snapshot probe for currently-running <c>RobloxPlayerBeta.exe</c> PIDs at the moment of the
/// call. Used at app startup (BEFORE <c>mutex.Acquire()</c>) to detect "Roblox started before
/// RoRoRo" — that scenario silently breaks multi-instance because the auth-ticket hand-off
/// routes through an already-running process with a bound user identity, so every subsequent
/// Launch As opens as the same Roblox user. Cycle 4 (2026-05-08).
/// </summary>
/// <remarks>
/// Soft-fail discipline lives at the call site (see <c>StartupGate</c>), not here. A false
/// positive (modal shown when no Roblox is running) blocks the user from starting the app at
/// all — unrecoverable. A false negative (probe returns empty when Roblox is running) is
/// recoverable via the existing manual workaround. Implementations should let exceptions
/// bubble; the gate wraps them with fail-open semantics.
/// </remarks>
public interface IRobloxRunningProbe
{
    /// <summary>
    /// Snapshot of every <c>RobloxPlayerBeta.exe</c> PID running on this Windows user session
    /// at call time. Empty list = clean (no foreign Roblox detected).
    /// </summary>
    IReadOnlyList<int> GetRunningPlayerPids();
}
