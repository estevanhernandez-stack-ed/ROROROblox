using System.IO;
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

        var pid = starter.Start("626labs.test", WhereExe);

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

    [Fact]
    public void Start_MissingExecutable_ThrowsAndLogsQuarantineHint()
    {
        var log = new CapturingLogger<DefaultPluginProcessStarter>();
        var starter = new DefaultPluginProcessStarter(log);
        var missing = Path.Combine(Path.GetTempPath(), "urtask-missing-" + Guid.NewGuid().ToString("N") + ".exe");

        Assert.Throws<FileNotFoundException>(() => starter.Start("626labs.test", missing));

        var line = Assert.Single(log.Snapshot(), l => l.Contains("not found"));
        Assert.Contains("626labs.test", line);
        Assert.Contains(missing, line);
    }

    [Fact]
    public void Start_NoLogger_StillWorks()
    {
        // The optional-logger seam must not become load-bearing: null logger,
        // full start/exit cycle, no throw.
        var starter = new DefaultPluginProcessStarter();
        using var exited = new ManualResetEventSlim(false);
        starter.ProcessExited += _ => exited.Set();

        starter.Start("626labs.test", WhereExe);

        Assert.True(exited.Wait(TimeSpan.FromSeconds(10)), "process never exited");
    }
}
