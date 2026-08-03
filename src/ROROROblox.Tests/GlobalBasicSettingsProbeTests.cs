using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
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

    /// <summary>
    /// Fix 2: <c>GlobalBasicSettingsFile.Resolve</c> hits the filesystem
    /// (<c>Directory.Exists</c> + <c>Directory.EnumerateFiles</c>) and can throw on its own --
    /// e.g. <c>UnauthorizedAccessException</c> when a Roblox installer recreates
    /// <c>%LOCALAPPDATA%\Roblox</c> mid-Squad-Launch, or a TOCTOU race between the <c>Exists</c>
    /// check and the lazy enumeration. Both methods promise <c>null</c> for "missing, locked, or
    /// malformed"; a <c>Resolve</c> call sitting outside the <c>try</c> broke that promise and let
    /// the exception escape into the launch path -- an 8-account Squad Launch aborting at account
    /// 3 rather than degrading. Deny list/read access on a real directory to force
    /// <see cref="UnauthorizedAccessException"/> out of <c>EnumerateFiles</c> deterministically
    /// (an existing directory is required so <c>Directory.Exists</c> returns true and the
    /// enumeration itself is what throws -- a nonexistent root never reaches this code path).
    /// </summary>
    [Fact]
    public void ReadFramerateCap_ReturnsNullRatherThanThrowing_WhenTheDirectoryIsUnreadable()
    {
        var locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(locked);

        var dirInfo = new DirectoryInfo(locked);
        var security = dirInfo.GetAccessControl();
        var identity = WindowsIdentity.GetCurrent().User!;
        var denyRule = new FileSystemAccessRule(
            identity,
            FileSystemRights.ListDirectory | FileSystemRights.ReadData,
            AccessControlType.Deny);
        security.AddAccessRule(denyRule);
        dirInfo.SetAccessControl(security);

        try
        {
            var probe = new GlobalBasicSettingsProbe(locked);

            Assert.Null(probe.ReadFramerateCap());
            Assert.Null(probe.GetLastWriteTimeUtc());
        }
        finally
        {
            // Deny ACEs block Directory.Delete's own enumeration too -- restore access before
            // Dispose() tries the recursive delete, or teardown throws the same exception class
            // this test exists to prove doesn't escape the probe.
            security.RemoveAccessRule(denyRule);
            dirInfo.SetAccessControl(security);
        }
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
