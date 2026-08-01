namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Force-closes every running RobloxPlayerBeta.exe in one shot — the "stop all instances"
/// teardown (multi-instance lane). Distinct from <see cref="IRobloxProcessTracker.Kill"/>, which
/// closes a single RoRoRo-launched account; this stops ALL running clients, tracked or not.
/// </summary>
public interface IRobloxInstanceStopper
{
    /// <summary>
    /// Force-closes every running RobloxPlayerBeta.exe. Returns the count actually stopped.
    /// Degrade-safe: a single kill failure does not abort the rest, and a probe failure resolves
    /// to 0 rather than throwing.
    /// </summary>
    int StopAll();

    /// <summary>
    /// Force-closes the single RobloxPlayerBeta.exe currently tracked for <paramref name="accountId"/>
    /// (the memory-watchdog Recycle action, Task 8). Reuses the same kill + bounded exit-wait
    /// budget as <see cref="StopAll"/>, scoped to one pid. Returns false (never throws) if the
    /// account has no tracked process — there is nothing to stop, which is not an error.
    /// </summary>
    bool StopAccount(Guid accountId);
}
