using System;
using System.Collections.Generic;
using System.IO;
using ROROROblox.App.Diagnostics;
using ROROROblox.App.Distribution;
using ROROROblox.App.Plugins;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// #52: Diagnostics reports the version About shows and the channel the copy was installed from.
/// The inputs are injected, so all four channels resolve here without packaging anything.
/// </summary>
public class InstallIdentityTests
{
    [Theory]
    [InlineData("store", false, InstallChannel.Store)]
    [InlineData("store", true, InstallChannel.Store)]
    [InlineData("sideload", false, InstallChannel.Sideload)]
    [InlineData("sideload", true, InstallChannel.Sideload)]
    [InlineData("dev", true, InstallChannel.DirectDownload)]
    [InlineData("dev", false, InstallChannel.Dev)]
    public void Channel_ResolvesFromFlavourAndVelopack(string flavour, bool velopackInstalled, InstallChannel expected)
    {
        var identity = new InstallIdentity(() => "1.2.3", flavour, () => velopackInstalled);

        Assert.Equal(expected, identity.Channel);
        Assert.Equal("1.2.3", identity.Version);
    }

    [Fact]
    public void Channel_PackagedBuilds_NeverAskVelopack()
    {
        var asked = false;
        _ = new InstallIdentity(() => "1.0.0", HostBuildIdentity.Store, () => { asked = true; return true; });
        Assert.False(asked);
    }

    [Fact]
    public void Channel_VelopackProbeThrows_FallsBackToDev()
    {
        var identity = new InstallIdentity(() => "1.0.0", HostBuildIdentity.Dev, () => throw new InvalidOperationException());

        Assert.Equal(InstallChannel.Dev, identity.Channel);
    }

    [Fact]
    public void Version_IsExactlyWhatTheAboutPageShows()
    {
        // About does AppVersion.Display(new Win32DistributionMode()); the identity must call the
        // same helper with the same probe.
        var build = new HostBuildIdentity(() => null, "x.exe", "1.0.0");
        var identity = new InstallIdentity(new Win32DistributionMode(), build);

        Assert.Equal(AppVersion.Display(new Win32DistributionMode()), identity.Version);
    }

    [Fact]
    public void SnapshotText_PutsChannelDirectlyUnderAppVersion()
    {
        var snapshot = new DiagnosticsSnapshot(
            "1.36.0", ".NET", "OS", null, false, null, false, 0, 0, false, "", "",
            DateTimeOffset.UtcNow, 0, 0, [], InstallChannel.DirectDownload);
        using var writer = new StringWriter();

        DiagnosticsPage.WriteSnapshotText(writer, snapshot);

        var lines = writer.ToString().Split(["\r\n", "\n"], StringSplitOptions.None);
        var versionAt = Array.FindIndex(lines, l => l.StartsWith("App version", StringComparison.Ordinal));
        Assert.True(versionAt >= 0);
        Assert.Equal("Channel           : DirectDownload", lines[versionAt + 1]);
    }

    [Theory]
    [InlineData(InstallChannel.Store, "Store")]
    [InlineData(InstallChannel.Sideload, "Sideload")]
    [InlineData(InstallChannel.DirectDownload, "DirectDownload")]
    [InlineData(InstallChannel.Dev, "Dev")]
    [InlineData(null, "not detected")]
    public void SnapshotChannelWord_IsEnglishAndInvariant(InstallChannel? channel, string expected)
        => Assert.Equal(expected, DiagnosticsPage.ChannelText(channel));
}
