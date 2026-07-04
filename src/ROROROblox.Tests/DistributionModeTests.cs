using ROROROblox.App.Distribution;

namespace ROROROblox.Tests;

/// <summary>
/// The xUnit test host is an UNPACKAGED process, so the real Win32-backed distribution mode must
/// report IsPackaged == false here. This proves the GetCurrentPackageFullName P/Invoke is wired
/// correctly (returns APPMODEL_ERROR_NO_PACKAGE when unpackaged). The marketplace-gating logic that
/// CONSUMES IDistributionMode is unit-tested separately with a fake in the PluginsViewModel tests.
/// </summary>
public class DistributionModeTests
{
    [Fact]
    public void Win32DistributionMode_InUnpackagedTestHost_ReportsNotPackaged()
    {
        var mode = new Win32DistributionMode();

        Assert.False(mode.IsPackaged);
    }
}
