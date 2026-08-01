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

    [Theory]
    [InlineData(8, 4096)]    // 35% of 8 GB = 2867 -> floor
    [InlineData(16, 5734)]   // 35% of 16 GB
    [InlineData(32, 11468)]  // 35% of 32 GB
    [InlineData(64, 22937)]  // 35% of 64 GB
    public void CapMb_FloorsAt4096(int totalGb, int expectedMb)
        => Assert.Equal(expectedMb, MemoryDefaults.CapMb(totalGb * Gb));

    [Fact]
    public void UnreadableTotal_FallsBackToConservativeDefaults()
    {
        // Zero total means the probe failed. Do not derive from a value we do not have.
        Assert.Equal(1024, MemoryDefaults.ReserveMb(0));
        Assert.Equal(4096, MemoryDefaults.CapMb(0));
    }
}
