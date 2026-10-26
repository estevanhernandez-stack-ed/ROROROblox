using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// #246: RoRoRo registers for restart with RESTART_NO_REBOOT only. Reboot restart stays off because
/// run-on-login is opt-in; crash, hang and patch restart stay on. Nothing here calls Register, since
/// a registered test host would be restarted by WER if it crashed.
/// </summary>
public class ApplicationRestartFlagsTests
{
    private const uint RestartNoCrash = 1, RestartNoHang = 2, RestartNoPatch = 4, RestartNoReboot = 8;

    [Fact]
    public void FlagsAreNoRebootOnly()
    {
        var flags = ROROROblox.App.ApplicationRestart.Flags;
        Assert.NotEqual(0u, flags & RestartNoReboot);
        Assert.Equal(0u, flags & RestartNoCrash);
        Assert.Equal(0u, flags & RestartNoHang);
        Assert.Equal(0u, flags & RestartNoPatch);
    }

    [Fact]
    public void TheRestartArgumentIsRecognisedOnlyWhenPresent()
    {
        // #255: Windows hands this back on a crash or servicing restart; startup reads it to skip
        // the leftover dialog nobody is there to answer.
        Assert.True(ROROROblox.App.ApplicationRestart.IsWindowsRestart(["--restarted-by-windows"]));
        Assert.True(ROROROblox.App.ApplicationRestart.IsWindowsRestart(["roblox-rororo://x", "--RESTARTED-BY-WINDOWS"]));
        Assert.False(ROROROblox.App.ApplicationRestart.IsWindowsRestart([]));
        Assert.False(ROROROblox.App.ApplicationRestart.IsWindowsRestart(["roblox-rororo://x"]));
        Assert.False(ROROROblox.App.ApplicationRestart.IsWindowsRestart(null));
    }

    [Fact]
    public void RegisterPassesTheRestartArgument()
    {
        // The registration, not just the constant: a restart without the argument would land on the
        // dialog again. Source-level check, because calling Register would arm WER on the test host.
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        var src = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "ApplicationRestart.cs"));
        Assert.Matches(@"RegisterApplicationRestart\(\s*RestartArgument\s*,", src);
    }

    [Fact]
    public void OnStartupRegistersOnceAfterTheSingleInstanceGuard()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        var src = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "App.xaml.cs"));
        Assert.Single(Regex.Matches(src, @"ApplicationRestart\.Register\("));
        Assert.True(src.IndexOf("ApplicationRestart.Register(", StringComparison.Ordinal)
            > src.IndexOf("AcquireOrSignalExisting(", StringComparison.Ordinal));
    }
}
