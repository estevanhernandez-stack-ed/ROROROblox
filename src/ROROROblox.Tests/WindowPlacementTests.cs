using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// F-060. The main window came back at 900x600 centred on every launch. Persisting placement is
/// only half of it — restoring one that is no longer on any screen would be a worse bug than the
/// one being fixed, so the guard is the part worth testing.
/// </summary>
public class WindowPlacementTests
{
    // A single 1920x1080 display at the origin.
    private const double L = 0, T = 0, W = 1920, H = 1080;

    [Fact]
    public void AnOrdinaryPlacementRestores()
    {
        Assert.True(new WindowPlacement(100, 100, 900, 600, false).IsRestorableOnto(L, T, W, H));
    }

    [Fact]
    public void AWindowFromAMonitorThatIsGoneDoesNot()
    {
        // Saved on a second display to the right, which has since been unplugged. This is the
        // laptop-and-dock case, not an exotic one.
        Assert.False(new WindowPlacement(2400, 300, 900, 600, false).IsRestorableOnto(L, T, W, H));
    }

    [Fact]
    public void AWindowHangingOffTheEdgeStillRestores()
    {
        // People park windows half off-screen deliberately. Containment would "fix" that on every
        // launch and feel like the app fighting them.
        Assert.True(new WindowPlacement(1500, 200, 900, 600, false).IsRestorableOnto(L, T, W, H));
    }

    [Fact]
    public void AWindowWithAlmostNothingOnScreenDoesNot()
    {
        // A sliver at the far edge: technically overlapping, practically ungrabbable.
        Assert.False(new WindowPlacement(1880, 200, 900, 600, false).IsRestorableOnto(L, T, W, H));
    }

    [Fact]
    public void ANegativeOriginIsFineOnAMultiMonitorDesktop()
    {
        // A display left of the primary has negative coordinates. Rejecting those would break the
        // very setup this guard exists to serve.
        Assert.True(new WindowPlacement(-1800, 100, 900, 600, false).IsRestorableOnto(-1920, 0, 3840, 1080));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(120, 600)]
    [InlineData(900, 40)]
    public void ACollapsedSizeIsNotAPreference(double width, double height)
    {
        // Zero or near-zero comes from a crash mid-save or a minimised-at-exit read, never from a
        // user choosing it. Restoring it would open the app as a slot.
        Assert.False(new WindowPlacement(100, 100, width, height, false).IsRestorableOnto(L, T, W, H));
    }

    [Fact]
    public void GarbageIsRejectedRatherThanRestored()
    {
        Assert.False(new WindowPlacement(double.NaN, 100, 900, 600, false).IsRestorableOnto(L, T, W, H));
    }
}

/// <summary>The round trip through the real settings store (F-060).</summary>
public class WindowPlacementPersistenceTests
{
    [Fact]
    public async Task APlacementSurvivesASaveAndReload()
    {
        // The whole complaint: resize the window, relaunch, get 900x600 back.
        var path = Path.Combine(Path.GetTempPath(), $"rororo-placement-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings(path);
            await settings.SetMainWindowPlacementAsync(new WindowPlacement(120, 80, 1400, 900, false));

            var reloaded = await new AppSettings(path).GetMainWindowPlacementAsync();

            Assert.Equal(new WindowPlacement(120, 80, 1400, 900, false), reloaded);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task AnUntouchedInstallHasNoPlacement()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rororo-placement-{Guid.NewGuid():N}.json");
        try { Assert.Null(await new AppSettings(path).GetMainWindowPlacementAsync()); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task TheMaximizedFlagRoundTripsSeparatelyFromTheBounds()
    {
        // Both halves matter: the flag restores the maximized state, the bounds are what
        // un-maximizing returns to. Losing either leaves the window stuck.
        var path = Path.Combine(Path.GetTempPath(), $"rororo-placement-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings(path);
            await settings.SetMainWindowPlacementAsync(new WindowPlacement(10, 20, 1000, 700, Maximized: true));

            var reloaded = await new AppSettings(path).GetMainWindowPlacementAsync();

            Assert.True(reloaded!.Value.Maximized);
            Assert.Equal(1000, reloaded.Value.Width);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
