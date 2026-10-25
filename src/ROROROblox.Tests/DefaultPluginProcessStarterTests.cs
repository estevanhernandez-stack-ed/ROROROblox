using System.IO;
using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;

namespace ROROROblox.Tests;

/// <summary>
/// Exit-code evidence tests (issue #36): the starter is the only place the host
/// ever learns a plugin's exit code, so these drive a real short-lived process
/// (where.exe — present on every Windows box and CI image, exits fast with a
/// nonzero code when given no arguments) and assert the lifecycle lines land.
/// </summary>
public class DefaultPluginProcessStarterTests
{
    private static string WhereExe => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");

    [Fact]
    public void Start_RealProcess_LogsStartAndExitWithCodeAndUptime()
    {
        var log = new CapturingLogger<DefaultPluginProcessStarter>();
        var starter = new DefaultPluginProcessStarter(log);
        using var exited = new ManualResetEventSlim(false);
        starter.ProcessExited += _ => exited.Set();

        var pid = starter.Start("626labs.test", WhereExe, PluginLaunchReason.Manual);

        Assert.True(exited.Wait(TimeSpan.FromSeconds(10)), "process never exited");

        // The exit log line is written just before ProcessExited fires, but poll
        // briefly anyway so a scheduler hiccup can't flake the assertion.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        IReadOnlyList<string> lines;
        do
        {
            lines = log.Snapshot();
            if (lines.Any(l => l.Contains("exited"))) break;
            Thread.Sleep(20);
        } while (DateTime.UtcNow < deadline);

        var startLine = Assert.Single(lines, l => l.Contains("started"));
        Assert.Contains("626labs.test", startLine);
        Assert.Contains($"pid {pid}", startLine);

        var exitLine = Assert.Single(lines, l => l.Contains("exited"));
        Assert.Contains("626labs.test", exitLine);
        Assert.Contains($"pid {pid}", exitLine);
        Assert.Contains("code 0x", exitLine);   // hex exit code present, whatever its value
        Assert.Contains("after", exitLine);      // uptime present
    }

    // ---- RORORO_LAUNCH_REASON (v1.33 item 8) -------------------------------------------------

    [Theory]
    [InlineData(PluginLaunchReason.Autostart, "autostart")]
    [InlineData(PluginLaunchReason.Manual, "manual")]
    [InlineData(PluginLaunchReason.Install, "install")]
    [InlineData(PluginLaunchReason.Update, "update")]
    [InlineData(PluginLaunchReason.Restart, "restart")]
    public void EveryReasonReachesTheChildsEnvironment(PluginLaunchReason reason, string expected)
    {
        // Asserted on the ProcessStartInfo rather than a real child, because a child cannot be
        // asked: IPluginProcessStarter.Start takes no arguments to pass a probe, and reading another
        // process's environment is not something to build a test on. The wire strings are spelled
        // out here on purpose — a test that read them back through ToWireValue would pass if someone
        // renamed one, and these are a published contract the moment a plugin matches on them.
        var psi = DefaultPluginProcessStarter.BuildStartInfo(WhereExe, reason);

        Assert.Equal(expected, psi.Environment["RORORO_LAUNCH_REASON"]);
    }

    [Fact]
    public void TheLaunchInfoKeepsUseShellExecuteFalseSoTheEnvironmentSurvives()
    {
        // Not style. ProcessStartInfo throws on Start when Environment has been populated and
        // UseShellExecute is true, so these two settings cannot both be had — and the throw would
        // land on every plugin launch, not just the ones reading the variable.
        var psi = DefaultPluginProcessStarter.BuildStartInfo(WhereExe, PluginLaunchReason.Manual);

        Assert.False(psi.UseShellExecute);
    }

    [Fact]
    public void EveryDeclaredReasonHasADistinctWireValue()
    {
        // Exhaustiveness, so a sixth reason cannot be added without a wire name. ToWireValue throws
        // on an unmapped member, and distinctness matters because two reasons sharing a string is a
        // plugin that cannot tell them apart — which is the entire point of sending it.
        var all = Enum.GetValues<PluginLaunchReason>();
        Assert.NotEmpty(all);

        var wire = all.Select(r => r.ToWireValue()).ToList();

        Assert.DoesNotContain(wire, string.IsNullOrWhiteSpace);
        Assert.Equal(wire.Count, wire.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AReasonDoesNotStopTheProcessStarting()
    {
        // The end-to-end half: a populated environment block must not break the launch itself.
        var starter = new DefaultPluginProcessStarter();

        var pid = starter.Start("626labs.test", WhereExe, PluginLaunchReason.Update);

        Assert.True(pid > 0, "a plugin launched with a reason should still start");
    }

    [Fact]
    public void Start_MissingExecutable_ThrowsAndLogsQuarantineHint()
    {
        var log = new CapturingLogger<DefaultPluginProcessStarter>();
        var starter = new DefaultPluginProcessStarter(log);
        var missing = Path.Combine(Path.GetTempPath(), "urtask-missing-" + Guid.NewGuid().ToString("N") + ".exe");

        Assert.Throws<FileNotFoundException>(() => starter.Start("626labs.test", missing, PluginLaunchReason.Manual));

        var line = Assert.Single(log.Snapshot(), l => l.Contains("not found"));
        Assert.Contains("626labs.test", line);
        Assert.Contains(missing, line);
    }

    // ---- Launch by the on-disk path (#242) ----------------------------------------------------
    //
    // On a Store-only install the plugins folder is virtual: it exists only inside the package
    // context, and a plugin started by that path outside it dies 0x80008085 before any of its code
    // runs. A package context cannot be built in a unit test; a directory junction can, and to
    // GetFinalPathNameByHandle the two are the same question (a path that is not where the file
    // is). Each test below fails on the pre-#242 code.

    private static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);

    /// <summary>A junction in temp pointing at System32. mklink /J needs no elevation.</summary>
    private sealed class System32Junction : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rororo-242-" + Guid.NewGuid().ToString("N"));

        public System32Junction()
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{Path}\" \"{System32}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            p.WaitForExit(10_000);
            Assert.True(Directory.Exists(Path), "test setup: mklink /J did not create the junction");
        }

        // Non-recursive on purpose: deleting a junction this way removes the link, never System32.
        public void Dispose() { try { Directory.Delete(Path, recursive: false); } catch { } }
    }

    [Fact]
    public void Start_ThroughAVirtualPath_LaunchesByTheOnDiskPath()
    {
        using var junction = new System32Junction();
        var log = new CapturingLogger<DefaultPluginProcessStarter>();
        var starter = new DefaultPluginProcessStarter(log);

        starter.Start("626labs.test", Path.Combine(junction.Path, "where.exe"), PluginLaunchReason.Manual);

        var startLine = Assert.Single(log.Snapshot(), l => l.Contains("process started"));
        Assert.Contains(Path.Combine(System32, "where.exe"), startLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(junction.Path, startLine, StringComparison.OrdinalIgnoreCase);
        // The field evidence: a log that names both paths when they differ.
        Assert.Single(log.Snapshot(), l => l.Contains("resolves to") && l.Contains(junction.Path));
    }

    [Fact]
    public void FindRunningUnder_AskedByTheVirtualPath_StillSeesAPluginRunningFromTheOnDiskPath()
    {
        // The uninstall/update wait and the orphan sweep ask by the path the host sees. Missing a
        // live plugin here means deleting its files from under it.
        using var junction = new System32Junction();
        using var longRunner = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Path.Combine(System32, "PING.EXE"), "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            var hits = new DefaultPluginProcessStarter().FindRunningUnder(junction.Path);

            Assert.Contains(longRunner.Id, hits);
        }
        finally
        {
            try { longRunner.Kill(); } catch { }
        }
    }

    [Fact]
    public void OnDiskPath_LeavesWhatItCannotResolveUnchanged()
    {
        var missing = Path.Combine(Path.GetTempPath(), "rororo-242-missing-" + Guid.NewGuid().ToString("N"), "x.exe");

        Assert.Equal(missing, OnDiskPath.Resolve(missing));
        Assert.Equal("", OnDiskPath.Resolve(""));
    }

    [Fact]
    public void OnDiskPath_AnOrdinaryPathResolvesToItself()
    {
        Assert.Equal(System32, OnDiskPath.Resolve(System32), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"\\?\D:\data\plugins\a.exe", @"D:\data\plugins\a.exe")]
    [InlineData(@"\\?\UNC\server\share\a.exe", @"\\server\share\a.exe")]
    [InlineData(@"C:\plain\a.exe", @"C:\plain\a.exe")]
    public void OnDiskPath_StripsTheDevicePrefix(string finalPath, string expected)
    {
        Assert.Equal(expected, OnDiskPath.StripDevicePrefix(finalPath));
    }

    [Fact]
    public void Start_NoLogger_StillWorks()
    {
        // The optional-logger seam must not become load-bearing: null logger,
        // full start/exit cycle, no throw.
        var starter = new DefaultPluginProcessStarter();
        using var exited = new ManualResetEventSlim(false);
        starter.ProcessExited += _ => exited.Set();

        starter.Start("626labs.test", WhereExe, PluginLaunchReason.Manual);

        Assert.True(exited.Wait(TimeSpan.FromSeconds(10)), "process never exited");
    }
}
