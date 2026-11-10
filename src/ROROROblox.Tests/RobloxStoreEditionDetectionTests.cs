using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The Roblox-needed modal names the Microsoft Store edition when that is what the PC has
/// (2026-11-10). The Store edition registers the <c>roblox</c> protocol and never
/// <c>roblox-player</c>, so its owner is told "Roblox isn't installed" while looking at Roblox.
/// </summary>
public sealed class RobloxStoreEditionDetectionTests : IDisposable
{
    private readonly string _packages = Path.Combine(Path.GetTempPath(), "rororo-store-edition-" + Guid.NewGuid().ToString("N"));

    public RobloxStoreEditionDetectionTests() => Directory.CreateDirectory(_packages);

    public void Dispose()
    {
        try { Directory.Delete(_packages, recursive: true); } catch { }
    }

    [Fact]
    public void NoRobloxPackage_IsNotTheStoreEdition()
    {
        Directory.CreateDirectory(Path.Combine(_packages, "Microsoft.WindowsCalculator_8wekyb3d8bbwe"));

        Assert.False(RobloxInstallLocator.HasStoreEdition(_packages));
    }

    [Fact]
    public void TheRobloxPackageFolder_IsTheStoreEdition()
    {
        Directory.CreateDirectory(Path.Combine(_packages, "ROBLOXCORPORATION.ROBLOX_55nm5eh3cm0pr"));

        Assert.True(RobloxInstallLocator.HasStoreEdition(_packages));
    }

    [Fact]
    public void ALookalikePublisher_IsNotTheStoreEdition()
    {
        // The match is on the package family prefix, not on the word Roblox appearing somewhere.
        Directory.CreateDirectory(Path.Combine(_packages, "SomeoneElse.ROBLOXHelper_abc123"));

        Assert.False(RobloxInstallLocator.HasStoreEdition(_packages));
    }

    [Fact]
    public void AMissingPackagesFolder_IsNotTheStoreEdition()
    {
        Assert.False(RobloxInstallLocator.HasStoreEdition(Path.Combine(_packages, "does-not-exist")));
    }
}
