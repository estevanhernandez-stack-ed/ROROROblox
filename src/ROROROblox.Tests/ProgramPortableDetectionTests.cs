using System;
using System.IO;
using ROROROblox.App;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Portable-layout detection drives the AppUserModelID override in Program.Main: a Velopack
/// portable run must get the suffixed AUMID (so a stale install's Start-menu shortcut can't
/// blank its taskbar icon), while installed and dev runs must keep Velopack's default.
/// </summary>
public class ProgramPortableDetectionTests : IDisposable
{
    private readonly string _root;

    public ProgramPortableDetectionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rororo-portable-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "current"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir; best effort */ }
    }

    [Fact]
    public void PortableLayout_MarkerNextToParent_IsDetected()
    {
        File.WriteAllText(Path.Combine(_root, ".portable"), string.Empty);

        Assert.True(Program.IsPortableInstall(Path.Combine(_root, "current")));
    }

    [Fact]
    public void InstalledLayout_NoMarker_IsNotPortable()
    {
        Assert.False(Program.IsPortableInstall(Path.Combine(_root, "current")));
    }

    [Fact]
    public void UnreadablePath_FailsClosed_NotPortable()
    {
        Assert.False(Program.IsPortableInstall("Z:\\does\\not\\exist\\current"));
    }
}
