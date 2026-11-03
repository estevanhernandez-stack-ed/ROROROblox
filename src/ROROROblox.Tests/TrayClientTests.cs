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

    // #222 age floor. Now is fixed so every row is deterministic.
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Floor = TimeSpan.FromMinutes(2);
    private const string TrayCmd = Exe + " --launch-to-tray";

    private static bool Idle(bool hasWindow, string? cmd, DateTimeOffset? started, bool tracked = false, TimeSpan? minAge = null)
        => TrayClient.IsIdleTrayClient(new RobloxProcessInfo(1, hasWindow, cmd, started), _ => tracked, minAge, Now);

    [Fact]
    public void Age_UnderTwoMinutes_NeverSwept()
        => Assert.False(Idle(false, TrayCmd, Now - TimeSpan.FromSeconds(119), minAge: Floor));

    [Fact]
    public void Age_FutureStartTime_NeverSwept()
        => Assert.False(Idle(false, TrayCmd, Now + TimeSpan.FromMinutes(5), minAge: Floor));

    [Fact]
    public void Age_Windowed_NeverSwept()
        => Assert.False(Idle(true, TrayCmd, Now - TimeSpan.FromMinutes(40), minAge: Floor));

    [Fact]
    public void Age_Tracked_NeverSwept()
        => Assert.False(Idle(false, TrayCmd, Now - TimeSpan.FromMinutes(40), tracked: true, minAge: Floor));

    [Fact]
    public void Age_NullCommandLine_FailsClosed()
        => Assert.False(Idle(false, null, Now - TimeSpan.FromMinutes(40), minAge: Floor));

    [Fact]
    public void Age_UnknownStartTime_FailsClosed()
        => Assert.False(Idle(false, TrayCmd, null, minAge: Floor));

    [Fact]
    public void Age_NonTrayLoader_NeverSwept()
        => Assert.False(Idle(false, Exe + " roblox-player:1+launchmode:play", Now - TimeSpan.FromMinutes(40), minAge: Floor));

    [Theory]
    [InlineData(120)]
    [InlineData(2400)]
    public void Age_OverFloorAndIdleTray_Swept(int seconds)
        => Assert.True(Idle(false, TrayCmd, Now - TimeSpan.FromSeconds(seconds), minAge: Floor));

    [Fact]
    public void NullMinAge_KeepsOldRuleExactly()
    {
        // Brand new and unknown start time are both swept when there is no floor: the pre-launch clear is unchanged.
        Assert.True(Idle(false, TrayCmd, Now, minAge: null));
        Assert.True(Idle(false, TrayCmd, null, minAge: null));
        Assert.False(Idle(true, TrayCmd, null, minAge: null));
        Assert.False(Idle(false, TrayCmd, null, tracked: true, minAge: null));
        Assert.False(Idle(false, null, null, minAge: null));
    }

    [Fact]
    public void Probe_ReadStartedAt_UnreadableIsNull_ReadableIsUtc()
    {
        Assert.Null(RobloxRunningProbe.ReadStartedAt(() => throw new System.ComponentModel.Win32Exception(5)));
        Assert.Null(RobloxRunningProbe.ReadStartedAt(() => throw new InvalidOperationException()));
        var utc = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTimeOffset(utc), RobloxRunningProbe.ReadStartedAt(() => utc));
    }
}
