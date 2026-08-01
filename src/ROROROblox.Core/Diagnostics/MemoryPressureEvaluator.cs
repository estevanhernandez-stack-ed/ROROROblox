namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Pure predicate: has a <see cref="MemoryPressureSnapshot"/> returned to "nothing to warn
/// about"? <see cref="IMemoryWatchdog.PressureCrossed"/> is edge-triggered -- it fires ON a
/// crossing and stays silent while the condition holds -- so nothing else notices when pressure
/// recedes (e.g. after a Recycle, or growth naturally plateauing). This is what MainViewModel's
/// 30s ticker calls to decide whether to clear the tray's memory-warning badge.
/// <para>
/// "Clear" requires BOTH: no account is currently over its private-bytes cap, AND the aggregate
/// growth projection is either absent or back at/above the warn-minutes threshold. A missing
/// projection (<see cref="MemoryPressureSnapshot.HasProjection"/> == false) must NOT be read as
/// "cleared" on its own -- the cap trigger and the projection trigger are independent axes
/// (mirrors <see cref="MemoryWatchdog"/>'s own per-tick latch logic: an account can be over cap
/// with no valid aggregate projection at all, e.g. a single very-fast-growing client sampled for
/// under <see cref="MemoryWatchdog.MinimumObservation"/>).
/// </para>
/// </summary>
public static class MemoryPressureEvaluator
{
    public static bool IsClear(MemoryPressureSnapshot snapshot, int projectionWarnMinutes)
    {
        foreach (var account in snapshot.Accounts)
        {
            if (account.OverCap)
            {
                return false;
            }
        }

        // Negation of MemoryWatchdog.Sample()'s own latch condition
        // (`hasProjection && minutes < ProjectionWarnMinutes`) -- back at/above the threshold,
        // or no valid projection to distrust in the first place.
        return !snapshot.HasProjection || snapshot.MinutesToCeiling >= projectionWarnMinutes;
    }
}
