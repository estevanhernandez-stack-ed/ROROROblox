using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class HostBuildIdentityTests
{
    [Fact]
    public void Unpackaged_IsDev()
    {
        var id = new HostBuildIdentity(() => null, @"C:\dev\ROROROblox.App.exe", "1.33.0+abc");

        Assert.Equal("dev", id.InstallFlavour);
        Assert.Equal(@"C:\dev\ROROROblox.App.exe", id.ProcessPath);
        Assert.Equal("1.33.0+abc", id.InformationalVersion);
    }

    [Fact]
    public void StoreSigned_IsStore() =>
        Assert.Equal("store", new HostBuildIdentity(() => "Store", "p", "v").InstallFlavour);

    [Theory]
    [InlineData("Developer")]
    [InlineData("Enterprise")]
    [InlineData("None")]
    public void PackagedButNotStoreSigned_IsSideload(string kind) =>
        Assert.Equal("sideload", new HostBuildIdentity(() => kind, "p", "v").InstallFlavour);

    [Fact]
    public void ProbeThrows_FallsBackToDev() =>
        Assert.Equal("dev", new HostBuildIdentity(() => throw new InvalidOperationException(), "p", "v").InstallFlavour);

    [Fact]
    public void NullProcessPath_BecomesEmpty() =>
        Assert.Equal(string.Empty, new HostBuildIdentity(() => null, null, "v").ProcessPath);

    [Fact]
    public void DefaultCtor_UnderXunit_IsDevWithRealValues()
    {
        var id = new HostBuildIdentity();

        Assert.Equal("dev", id.InstallFlavour);
        Assert.False(string.IsNullOrWhiteSpace(id.ProcessPath));
        Assert.False(string.IsNullOrWhiteSpace(id.InformationalVersion));
    }
}
