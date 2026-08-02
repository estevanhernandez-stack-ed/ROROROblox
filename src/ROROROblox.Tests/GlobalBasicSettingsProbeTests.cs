using System;
using System.IO;
using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

public sealed class GlobalBasicSettingsProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rororo-gbs-" + Guid.NewGuid().ToString("N"));

    public GlobalBasicSettingsProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string WriteSettings(string name, int? cap)
    {
        var body = cap is null
            ? "<Item class=\"UserGameSettings\"><Properties /></Item>"
            : $"<Item class=\"UserGameSettings\"><Properties><int name=\"FramerateCap\">{cap}</int></Properties></Item>";
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, $"<roblox>{body}</roblox>");
        return path;
    }

    [Fact]
    public void ReadFramerateCap_ReturnsTheValueFromTheHighestNumberedFile()
    {
        WriteSettings("GlobalBasicSettings_9.xml", 60);
        WriteSettings("GlobalBasicSettings_13.xml", 20);

        var probe = new GlobalBasicSettingsProbe(_root);

        Assert.Equal(20, probe.ReadFramerateCap());
    }

    [Fact]
    public void ReadFramerateCap_IgnoresTheStudioVariant()
    {
        WriteSettings("GlobalBasicSettings_13.xml", 20);
        WriteSettings("GlobalBasicSettings_13_Studio.xml", 144);

        var probe = new GlobalBasicSettingsProbe(_root);

        Assert.Equal(20, probe.ReadFramerateCap());
    }

    [Fact]
    public void ReadFramerateCap_ReturnsNullWhenThereIsNoFile()
    {
        var probe = new GlobalBasicSettingsProbe(_root);

        Assert.Null(probe.ReadFramerateCap());
    }

    [Fact]
    public void ReadFramerateCap_ReturnsNullWhenTheNodeIsAbsent()
    {
        WriteSettings("GlobalBasicSettings_13.xml", cap: null);

        var probe = new GlobalBasicSettingsProbe(_root);

        Assert.Null(probe.ReadFramerateCap());
    }

    [Fact]
    public void ReadFramerateCap_ReturnsNullOnMalformedXmlRatherThanThrowing()
    {
        File.WriteAllText(Path.Combine(_root, "GlobalBasicSettings_13.xml"), "<roblox><not-closed>");

        var probe = new GlobalBasicSettingsProbe(_root);

        Assert.Null(probe.ReadFramerateCap());
    }

    [Fact]
    public void GetLastWriteTimeUtc_TracksTheFileAndIsNullWhenAbsent()
    {
        var probe = new GlobalBasicSettingsProbe(_root);
        Assert.Null(probe.GetLastWriteTimeUtc());

        var path = WriteSettings("GlobalBasicSettings_13.xml", 20);
        var stamped = new DateTime(2026, 8, 2, 16, 21, 10, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamped);

        Assert.Equal(stamped, probe.GetLastWriteTimeUtc()!.Value.UtcDateTime);
    }
}
