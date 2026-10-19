using ROROROblox.App.Distribution;
using Windows.ApplicationModel;

namespace ROROROblox.Tests;

/// <summary>
/// The packaged branch of <see cref="AppVersion.Display"/> reads
/// <c>Package.Current.Id.Version</c>, which needs a package identity. The xUnit host is an
/// unpackaged process, so that branch cannot run here; what these tests pin is the unpackaged
/// path, the formatting both branches share, and that a packaged claim with no real identity
/// falls back to the assembly version instead of throwing. The live packaged read is covered by
/// the packaged smoke (docs/store), not by this suite.
/// </summary>
public class AppVersionTests
{
    private sealed class FakeMode(bool packaged) : IDistributionMode
    {
        public bool IsPackaged { get; } = packaged;
    }

    private static string ExpectedAssemblyVersion() =>
        typeof(AppVersion).Assembly.GetName().Version!.ToString(3);

    [Fact]
    public void Display_Unpackaged_ReturnsAssemblyVersionWithThreeComponents()
    {
        var shown = AppVersion.Display(new FakeMode(packaged: false));

        Assert.Equal(ExpectedAssemblyVersion(), shown);
        Assert.Equal(3, shown.Split('.').Length);
    }

    [Fact]
    public void Display_PackagedClaimInUnpackagedHost_FallsBackToAssemblyVersion()
    {
        // The fake says packaged, but this process has no package identity, so the WinRT read
        // throws and the helper must swallow it. Same result as the unpackaged path.
        var shown = AppVersion.Display(new FakeMode(packaged: true));

        Assert.Equal(ExpectedAssemblyVersion(), shown);
    }

    [Fact]
    public void Format_PackageVersion_DropsTheRevisionComponent()
    {
        var shown = AppVersion.Format(new PackageVersion
        {
            Major = 1,
            Minor = 33,
            Build = 0,
            Revision = 0,
        });

        Assert.Equal("1.33.0", shown);
    }

    [Fact]
    public void Format_SystemVersion_IsThreeComponents()
    {
        Assert.Equal("1.33.0", AppVersion.Format(new Version(1, 33, 0, 0)));
    }

    [Fact]
    public void Format_NullVersion_IsZeroes()
    {
        Assert.Equal("0.0.0", AppVersion.Format((Version?)null));
    }
}
