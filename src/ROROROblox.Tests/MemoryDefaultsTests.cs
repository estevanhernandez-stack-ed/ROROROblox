using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class MemoryDefaultsTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(8, 1024)]    // 8% of 8 GB = 655 MB -> floor
    [InlineData(16, 1310)]   // 8% of 16 GB
    [InlineData(32, 2621)]   // 8% of 32 GB
    [InlineData(64, 4096)]   // 8% of 64 GB = 5242 -> ceiling
    [InlineData(128, 4096)]  // ceiling holds
    public void ReserveMb_ClampsBetween1024And4096(int totalGb, int expectedMb)
        => Assert.Equal(expectedMb, MemoryDefaults.ReserveMb(totalGb * Gb));

    /// <summary>
    /// REWRITTEN 2026-08-08 (F-082). This test used to assert the defect as though it were the
    /// specification — <c>Math.Max(35% of RAM, 4 GB)</c>, giving 5734 MB on 16 GB and 22937 MB on
    /// 64 GB. Those expectations were green for the whole life of v1.12–v1.16 while the warning
    /// they described could not fire: a Roblox client peaks near 3280 MB, so nothing ever crossed
    /// a cap that started at 5734 MB and climbed from there.
    /// <para>
    /// The axis was wrong. A per-client cap is anomaly detection, and a Roblox client's footprint
    /// does not depend on how much RAM its owner bought. The fraction now clamps the cap DOWNWARD
    /// (<c>Min</c>, not <c>Max</c>), so a small machine gets a stricter cap and a large one stops
    /// at the flat anomaly line instead of growing more permissive forever.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(8, 2867)]    // 35% of 8 GB — below the anomaly line, so the small machine tightens
    [InlineData(16, 4096)]   // 35% would be 5734; clamped down to the 4 GB anomaly line
    [InlineData(32, 4096)]   // 35% would be 11468
    [InlineData(64, 4096)]   // 35% would be 22937 — the cap must NOT climb with installed RAM
    public void CapMb_ClampsDownToTheAnomalyLine(int totalGb, int expectedMb)
        => Assert.Equal(expectedMb, MemoryDefaults.CapMb(totalGb * Gb));

    [Fact]
    public void UnreadableTotal_FallsBackToConservativeDefaults()
    {
        // Zero total means the probe failed. Do not derive from a value we do not have.
        Assert.Equal(1024, MemoryDefaults.ReserveMb(0));
        Assert.Equal(4096, MemoryDefaults.CapMb(0));
    }

    [Fact]
    public void NegativeTotal_FallsBackToConservativeDefaults()
    {
        // Pins the guard's shape as "<= 0", not "== 0" — a negative total is still an
        // unreadable/failed probe read, not a value to derive from.
        Assert.Equal(1024, MemoryDefaults.ReserveMb(-1));
        Assert.Equal(4096, MemoryDefaults.CapMb(-1));
    }
}
