using System.Diagnostics;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>Probe command-line reader and the idle-tray-client classifier (#241).</summary>
public class TrayClientTests
{
    private const string Exe = "\"RobloxPlayerBeta.exe\"";

    [Fact]
    public void Reader_ReturnsThisProcessCommandLine()
    {
        var cmd = ProcessCommandLine.TryRead(Environment.ProcessId);
        Assert.NotNull(cmd);
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath)!;
        Assert.Contains(name, cmd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reader_ReturnsNullForDeadPid()
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
        var pid = p.Id;
        p.WaitForExit();
        Assert.Null(ProcessCommandLine.TryRead(pid));
        Assert.Null(ProcessCommandLine.TryRead(int.MaxValue));
    }

    [Fact]
    public void Classifier_TrueForWindowlessUntrackedTray()
        => Assert.True(TrayClient.IsIdleTrayClient(new RobloxProcessInfo(1, false, Exe + " --launch-to-tray"), _ => false));

    [Fact]
    public void Classifier_FalseForWindowedTray()
        => Assert.False(TrayClient.IsIdleTrayClient(new RobloxProcessInfo(1, true, Exe + " --launch-to-tray"), _ => false));

    [Fact]
    public void Classifier_FalseForTrackedTray()
        => Assert.False(TrayClient.IsIdleTrayClient(new RobloxProcessInfo(1, false, Exe + " --launch-to-tray"), _ => true));

    [Fact]
    public void Classifier_FalseForNullCommandLine()
        => Assert.False(TrayClient.IsIdleTrayClient(new RobloxProcessInfo(1, false, null), _ => false));

    [Fact]
    public void Classifier_FalseForRobloxPlayerLoader()
        => Assert.False(TrayClient.IsIdleTrayClient(new RobloxProcessInfo(1, false, Exe + " roblox-player:1+launchmode:play+gameinfo:abc"), _ => false));
}
