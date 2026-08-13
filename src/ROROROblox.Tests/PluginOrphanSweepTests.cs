using System.IO;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

/// <summary>
/// F-101. Plugin processes left behind by a previous RoRoRo session are killed at startup, before
/// autostart adds more.
/// <para>
/// WHAT THIS DOES AND DOES NOT COVER. The primary fix for F-101 is <c>PluginJobObject</c>, which ties
/// plugin lifetime to the host at the KERNEL level so children die even when the host crashes or is
/// killed. That cannot be tested here — it requires really spawning a process and really killing its
/// parent, and asserting on the OS. This covers the sweep, which is the half that clears a pile made
/// BEFORE the job object shipped and the narrow window between <c>Process.Start</c> and job
/// assignment.
/// </para>
/// <para>
/// <b>So a green run here is not "F-101 is fixed."</b> The row's own fix direction says to verify by
/// process count across a launch/exit/launch cycle, because reading the teardown path is exactly how
/// this looked correct for two cycles.
/// </para>
/// </summary>
public class PluginOrphanSweepTests
{
    [Fact]
    public void SweepKillsEveryProcessRunningUnderThePluginsRoot()
    {
        var root = NewRoot();
        try
        {
            var starter = new FakeStarter(running: [101, 202, 303]);
            var supervisor = new PluginProcessSupervisor(starter);

            var killed = supervisor.SweepOrphans(root);

            Assert.Equal(3, killed);
            Assert.Equal([101, 202, 303], starter.Killed);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void SweepIsScopedToThePluginsRootItWasGiven()
    {
        var root = NewRoot();
        try
        {
            var starter = new FakeStarter(running: [7]);
            new PluginProcessSupervisor(starter).SweepOrphans(root);

            Assert.Equal(root, starter.SearchedUnder);
        }
        finally { Cleanup(root); }
    }

    /// <summary>
    /// A missing plugins root is the ordinary case for a user with no plugins installed. It must be
    /// a no-op, not a throw — this runs on the startup path and nothing here is worth failing a
    /// launch over.
    /// </summary>
    [Fact]
    public void SweepIsANoOpWhenThePluginsRootDoesNotExist()
    {
        var starter = new FakeStarter(running: [1]);
        var missing = Path.Combine(Path.GetTempPath(), "rororo-no-such-root-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(0, new PluginProcessSupervisor(starter).SweepOrphans(missing));
        Assert.Empty(starter.Killed);
        Assert.Null(starter.SearchedUnder);
    }

    /// <summary>
    /// An enumeration that throws must not stop the app starting. This is cleanup, and a user whose
    /// machine refuses to enumerate processes should still get their launcher.
    /// </summary>
    [Fact]
    public void SweepSwallowsAnEnumerationFailure()
    {
        var root = NewRoot();
        try
        {
            var starter = new FakeStarter(running: []) { ThrowOnFind = true };
            Assert.Equal(0, new PluginProcessSupervisor(starter).SweepOrphans(root));
        }
        finally { Cleanup(root); }
    }

    /// <summary>
    /// One kill failing must not abandon the rest. An orphan we cannot kill is the pre-fix status
    /// quo for that process; an orphan we never TRIED to kill because an earlier one threw is a
    /// regression this sweep introduced.
    /// </summary>
    [Fact]
    public void OneKillFailureDoesNotStopTheOthers()
    {
        var root = NewRoot();
        try
        {
            var starter = new FakeStarter(running: [1, 2, 3]) { ThrowOnKillPid = 2 };
            var killed = new PluginProcessSupervisor(starter).SweepOrphans(root);

            Assert.Equal(2, killed);
            Assert.Contains(3, starter.Killed);
        }
        finally { Cleanup(root); }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "rororo-sweep-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (IOException) { }
    }

    private sealed class FakeStarter(IReadOnlyList<int> running) : IPluginProcessStarter
    {
        public bool ThrowOnFind { get; set; }
        public int? ThrowOnKillPid { get; set; }
        public string? SearchedUnder { get; private set; }
        public List<int> Killed { get; } = [];

        public event Action<int>? ProcessExited { add { } remove { } }

        public int Start(string pluginId, string exePath) => throw new NotSupportedException();

        public void Kill(int pid)
        {
            if (ThrowOnKillPid == pid) throw new InvalidOperationException("simulated kill failure");
            Killed.Add(pid);
        }

        public IReadOnlyList<int> FindRunningUnder(string dirPath)
        {
            if (ThrowOnFind) throw new InvalidOperationException("simulated enumeration failure");
            SearchedUnder = dirPath;
            return running;
        }
    }
}
