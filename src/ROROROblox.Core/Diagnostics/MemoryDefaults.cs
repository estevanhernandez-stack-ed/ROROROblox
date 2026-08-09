using System;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Settings defaults derived from installed RAM. We do not know our users' hardware and a fixed
/// number is wrong across the 16-64 GB range the clan actually runs — silently, which is worse
/// than being wrong loudly. A zero total means the probe failed: fall back rather than derive
/// from a value we do not have.
/// </summary>
public static class MemoryDefaults
{
    private const long Mb = 1024L * 1024;
    private const int ReserveFloorMb = 1024;
    private const int ReserveCeilingMb = 4096;

    /// <summary>
    /// What one Roblox client actually costs. Measured 2026-08-07 on client 733 across 8
    /// concurrent clients in Pet Sim 99: median 2650 MB, peak 3280 MB.
    /// <para>
    /// These are constants because a Roblox client's footprint does not depend on how much RAM
    /// you own. That sentence is the whole fix — see <see cref="CapMb"/>.
    /// </para>
    /// </summary>
    public const int ExpectedClientMb = 2650;

    /// <summary>
    /// Above this, one client is anomalous rather than merely large — comfortably clear of the
    /// 3280 MB peak we measured, so a healthy client never trips it.
    /// </summary>
    private const int AnomalousClientMb = 4096;

    /// <summary>8% of installed RAM, clamped to [1 GB, 4 GB].</summary>
    public static int ReserveMb(long totalPhysicalBytes)
    {
        if (totalPhysicalBytes <= 0) return ReserveFloorMb;
        var eightPercent = (int)(totalPhysicalBytes * 0.08 / Mb);
        return Math.Clamp(eightPercent, ReserveFloorMb, ReserveCeilingMb);
    }

    /// <summary>
    /// The per-client ANOMALY threshold: 4 GB, clamped DOWNWARD by 35% of installed RAM.
    /// <para>
    /// CORRECTED 2026-08-08 (F-082). This was <c>Math.Max(35% of RAM, 4 GB)</c> — a per-client cap
    /// that scaled UP with installed memory, which is the wrong axis. A Roblox client uses what it
    /// uses; owning 64 GB does not make one client's 3 GB more acceptable. The effect was that the
    /// cap could not fire at any tier from 16 GB up (it derived 5734 MB on 16 GB and 16886 MB on
    /// 47 GB, against a client that peaks near 3280 MB), so v1.12 shipped a warning that was
    /// structurally unable to trigger. The v1.11.1 logs show its fixed predecessor firing at
    /// 2640 MB.
    /// </para>
    /// <para>
    /// <c>Min</c>, not <c>Max</c>: the fraction now only ever makes the cap STRICTER, which is what
    /// a small machine needs. 8 GB derives 2867 MB and fires early; every tier above lands on the
    /// flat 4 GB anomaly line and stops climbing.
    /// </para>
    /// <para>
    /// This axis is anomaly detection — <i>this one client is abnormal</i>. Whole-machine pressure
    /// is the headroom trigger's job, because the risk from ten clients is aggregate and no
    /// per-client number can see it.
    /// </para>
    /// </summary>
    public static int CapMb(long totalPhysicalBytes)
    {
        if (totalPhysicalBytes <= 0) return AnomalousClientMb;
        var thirtyFivePercent = (int)(totalPhysicalBytes * 0.35 / Mb);
        return Math.Min(AnomalousClientMb, thirtyFivePercent);
    }

    /// <summary>
    /// Whether one more client fits: its expected footprint has to survive alongside the reserve.
    /// Used by the pre-launch check, which warns rather than blocks — this is a forecast, and a
    /// forecast should not veto what someone does with their own machine.
    /// </summary>
    public static bool AnotherClientFits(long availableBytes, long reserveBytes) =>
        availableBytes - reserveBytes >= (long)ExpectedClientMb * Mb;
}
