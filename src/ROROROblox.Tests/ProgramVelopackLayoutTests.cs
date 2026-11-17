using System.IO;
using ROROROblox.App;

namespace ROROROblox.Tests;

/// <summary>
/// "Velopack-installed layout" means an <c>Update.exe</c> one level above the app folder. Installed
/// (<c>%LocalAppData%\RORORO\current</c>) and portable layouts both have it; a dev build under
/// <c>bin\</c> does not. Same shape as <see cref="ProgramPortableDetectionTests"/>.
/// </summary>
public class ProgramVelopackLayoutTests : IDisposable
{
    private readonly string _root;

    public ProgramVelopackLayoutTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rororo-velopack-layout-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "current"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* temp dir; best effort */ }
    }

    [Fact]
    public void UpdateExeOneLevelAbove_IsAVelopackLayout()
    {
        File.WriteAllText(Path.Combine(_root, "Update.exe"), string.Empty);

        Assert.True(Program.IsVelopackInstalledLayout(Path.Combine(_root, "current")));
    }

    [Fact]
    public void TrailingSeparatorOnTheBaseDirectory_StillFindsIt()
    {
        // AppContext.BaseDirectory ends with a separator.
        File.WriteAllText(Path.Combine(_root, "Update.exe"), string.Empty);

        Assert.True(Program.IsVelopackInstalledLayout(Path.Combine(_root, "current") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void NoUpdateExeAbove_IsADevLayout()
    {
        Assert.False(Program.IsVelopackInstalledLayout(Path.Combine(_root, "current")));
    }

    [Fact]
    public void UpdateExeInsideTheAppFolder_DoesNotCount()
    {
        // Only the level above counts; a stray Update.exe next to the app is not the layout.
        File.WriteAllText(Path.Combine(_root, "current", "Update.exe"), string.Empty);

        Assert.False(Program.IsVelopackInstalledLayout(Path.Combine(_root, "current")));
    }

    [Fact]
    public void UnreadablePath_FailsClosed_NotInstalled()
    {
        Assert.False(Program.IsVelopackInstalledLayout(@"Z:\does\not\exist\current"));
    }
}
