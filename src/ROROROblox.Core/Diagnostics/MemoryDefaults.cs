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
    private const int CapFloorMb = 4096;

    /// <summary>8% of installed RAM, clamped to [1 GB, 4 GB].</summary>
    public static int ReserveMb(long totalPhysicalBytes)
    {
        if (totalPhysicalBytes <= 0) return ReserveFloorMb;
        var eightPercent = (int)(totalPhysicalBytes * 0.08 / Mb);
        return Math.Clamp(eightPercent, ReserveFloorMb, ReserveCeilingMb);
    }

    /// <summary>35% of installed RAM — "no single client owns a third of the machine" — floored at 4 GB.</summary>
    public static int CapMb(long totalPhysicalBytes)
    {
        if (totalPhysicalBytes <= 0) return CapFloorMb;
        var thirtyFivePercent = (int)(totalPhysicalBytes * 0.35 / Mb);
        return Math.Max(thirtyFivePercent, CapFloorMb);
    }
}
