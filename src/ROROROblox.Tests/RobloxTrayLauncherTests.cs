using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class RobloxTrayLauncherTests
{
    // ---- RobloxInstallLocator: newest-by-write-time across a fixture install tree ----

    [Fact]
    public void FindNewestPlayerBeta_PicksNewestVersionFolder()
    {
        using var tree = new TempTree();
        var older = tree.PlayerBeta("Roblox/Versions/version-aaa", writeUtc: new DateTime(2026, 1, 1));
        var newer = tree.PlayerBeta("Roblox/Versions/version-bbb", writeUtc: new DateTime(2026, 6, 1));

        var found = RobloxInstallLocator.FindNewestPlayerBeta(
            Path.Combine(tree.Root, "Roblox", "Versions"),
            Path.Combine(tree.Root, "Packages"));

        Assert.Equal(newer, found);
    }

    [Fact]
    public void FindNewestPlayerBeta_FindsUwpInstall()
    {
        using var tree = new TempTree();
        var uwp = tree.PlayerBeta(
            "Packages/ROBLOXCORPORATION.ROBLOX_x/LocalCache/Local/Roblox/Versions/version-uwp",
            writeUtc: new DateTime(2026, 6, 1));

        var found = RobloxInstallLocator.FindNewestPlayerBeta(
            Path.Combine(tree.Root, "Roblox", "Versions"), // empty standalone root
            Path.Combine(tree.Root, "Packages"));

        Assert.Equal(uwp, found);
    }

    [Fact]
    public void FindNewestPlayerBeta_NoInstall_ReturnsNull()
    {
        using var tree = new TempTree();
        Assert.Null(RobloxInstallLocator.FindNewestPlayerBeta(
            Path.Combine(tree.Root, "nope"), Path.Combine(tree.Root, "also-nope")));
    }

    [Fact]
    public void FindNewestPlayerBeta_VersionFolderWithoutExe_Ignored()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(Path.Combine(tree.Root, "Roblox", "Versions", "version-empty"));

        Assert.Null(RobloxInstallLocator.FindNewestPlayerBeta(
            Path.Combine(tree.Root, "Roblox", "Versions"), Path.Combine(tree.Root, "Packages")));
    }

    // ---- RobloxTrayLauncher: launches the located exe with --launch-to-tray ----

    [Fact]
    public void RelaunchToTray_LaunchesLocatedExeWithTrayArg()
    {
        (string exe, string args)? started = null;
        var launcher = new RobloxTrayLauncher(
            locate: () => @"C:\fake\RobloxPlayerBeta.exe",
            start: (exe, args) => { started = (exe, args); return true; });

        Assert.True(launcher.RelaunchToTray());
        Assert.Equal(@"C:\fake\RobloxPlayerBeta.exe", started!.Value.exe);
        Assert.Equal("--launch-to-tray", started.Value.args);
    }

    [Fact]
    public void RelaunchToTray_NoInstall_ReturnsFalse_NeverStarts()
    {
        var startCalled = false;
        var launcher = new RobloxTrayLauncher(
            locate: () => null,
            start: (_, _) => { startCalled = true; return true; });

        Assert.False(launcher.RelaunchToTray());
        Assert.False(startCalled);
    }

    [Fact]
    public void RelaunchToTray_StartThrows_ReturnsFalse_DoesNotPropagate()
    {
        var launcher = new RobloxTrayLauncher(
            locate: () => @"C:\fake\RobloxPlayerBeta.exe",
            start: (_, _) => throw new System.ComponentModel.Win32Exception("blocked"));

        Assert.False(launcher.RelaunchToTray()); // best-effort: swallowed, not thrown
    }

    private sealed class TempTree : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "rororo-locator-" + Guid.NewGuid().ToString("N"));

        public string PlayerBeta(string relativeDir, DateTime writeUtc)
        {
            var dir = Path.Combine(Root, relativeDir.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dir);
            var exe = Path.Combine(dir, "RobloxPlayerBeta.exe");
            File.WriteAllText(exe, "stub");
            File.SetLastWriteTimeUtc(exe, writeUtc);
            return exe;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
