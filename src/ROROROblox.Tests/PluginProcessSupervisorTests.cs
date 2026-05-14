using System.IO;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class PluginProcessSupervisorTests : IDisposable
{
    private readonly string _tempDir;

    public PluginProcessSupervisorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-sup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private InstalledPlugin MakePlugin(string id, bool autostart)
    {
        return new InstalledPlugin
        {
            Manifest = new PluginManifest
            {
                SchemaVersion = 1, Id = id, Name = id, Version = "1.0",
                ContractVersion = "1.0", Publisher = "x", Description = "x",
                Capabilities = Array.Empty<string>(),
            },
            InstallDir = Path.Combine(_tempDir, id),
            Consent = new ConsentRecord
            {
                PluginId = id, GrantedCapabilities = Array.Empty<string>(),
                AutostartEnabled = autostart,
            },
        };
    }

    [Fact]
    public void StartAutostartPlugins_LaunchesOnlyAutostartEnabled()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);

        supervisor.StartAutostart(new[]
        {
            MakePlugin("626labs.a", autostart: true),
            MakePlugin("626labs.b", autostart: false),
            MakePlugin("626labs.c", autostart: true),
        });

        Assert.Equal(2, fake.Started.Count);
        Assert.Contains(fake.Started, s => s.id == "626labs.a");
        Assert.Contains(fake.Started, s => s.id == "626labs.c");
        Assert.DoesNotContain(fake.Started, s => s.id == "626labs.b");
    }

    [Fact]
    public void StopAll_TerminatesEveryTrackedProcess()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);

        supervisor.StartAutostart(new[]
        {
            MakePlugin("626labs.a", autostart: true),
            MakePlugin("626labs.b", autostart: true),
        });

        supervisor.StopAll();

        Assert.Equal(2, fake.KilledPids.Count);
    }

    [Fact]
    public void Restart_StopsAndStartsTheSamePlugin()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);
        var plugin = MakePlugin("626labs.a", autostart: true);

        supervisor.StartAutostart(new[] { plugin });
        Assert.Single(fake.Started);

        supervisor.Restart(plugin);

        Assert.Equal(2, fake.Started.Count);
        Assert.Single(fake.KilledPids);
    }

    [Fact]
    public void Stop_KillsTrackedPlugin_AndUntracksIt()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);
        var a = MakePlugin("626labs.a", autostart: true);
        var b = MakePlugin("626labs.b", autostart: true);

        supervisor.StartAutostart(new[] { a, b });
        var aPid = supervisor.RunningPids["626labs.a"];

        supervisor.Stop("626labs.a");

        Assert.Single(fake.KilledPids);
        Assert.Equal(aPid, fake.KilledPids[0]);
        Assert.False(supervisor.RunningPids.ContainsKey("626labs.a"));
        Assert.True(supervisor.RunningPids.ContainsKey("626labs.b"));
    }

    [Fact]
    public void Stop_IsNoOpForUntrackedPlugin()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);

        // No plugin started yet — Stop should not throw and should not call Kill.
        supervisor.Stop("626labs.nonexistent");

        Assert.Empty(fake.KilledPids);
    }

    [Fact]
    public void PluginExited_FiresWhenStarterReportsExit()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);
        var fired = new List<(string id, int pid)>();
        supervisor.PluginExited += (id, pid) => fired.Add((id, pid));

        supervisor.StartAutostart(new[] { MakePlugin("626labs.crash", autostart: true) });
        var pid = supervisor.RunningPids["626labs.crash"];

        fake.RaiseExit(pid);

        Assert.Single(fired);
        Assert.Equal("626labs.crash", fired[0].id);
        Assert.Equal(pid, fired[0].pid);
        // Mapping cleared: the plugin no longer shows up as running.
        Assert.False(supervisor.RunningPids.ContainsKey("626labs.crash"));
    }

    [Fact]
    public void Start_LaunchesPluginNotAlreadyRunning()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);
        var plugin = MakePlugin("626labs.a", autostart: false);

        supervisor.Start(plugin);

        Assert.Single(fake.Started);
        Assert.Equal("626labs.a", fake.Started[0].id);
        Assert.True(supervisor.RunningPids.ContainsKey("626labs.a"));
    }

    [Fact]
    public void Start_RestartsPluginAlreadyRunning()
    {
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);
        var plugin = MakePlugin("626labs.a", autostart: false);

        supervisor.Start(plugin);
        var firstPid = supervisor.RunningPids["626labs.a"];

        supervisor.Start(plugin);

        Assert.Equal(2, fake.Started.Count);          // started twice
        Assert.Single(fake.KilledPids);               // old process killed once
        Assert.Equal(firstPid, fake.KilledPids[0]);
        Assert.NotEqual(firstPid, supervisor.RunningPids["626labs.a"]);
    }

    [Fact]
    public async Task StopByInstallDirAsync_KillsTrackedAndOrphanProcesses()
    {
        // The install flow must clear an install dir of EVERY process running from it —
        // not just the one this session's PID map tracks. An orphan (a plugin that outlived
        // the RoRoRo session that started it) holds its DLLs locked and is invisible to Stop.
        var fake = new FakeProcessStarter();
        var supervisor = new PluginProcessSupervisor(fake);
        var plugin = MakePlugin("626labs.a", autostart: true);
        var installDir = plugin.InstallDir;

        supervisor.StartAutostart(new[] { plugin });
        var trackedPid = supervisor.RunningPids["626labs.a"];

        const int orphanPid = 9999; // running from installDir, never started by this supervisor
        fake.SetRunningUnder(installDir, new[] { trackedPid, orphanPid });

        await supervisor.StopByInstallDirAsync("626labs.a", installDir);

        Assert.Contains(trackedPid, fake.KilledPids);                   // tracked process killed
        Assert.Contains(orphanPid, fake.KilledPids);                    // orphan killed too
        Assert.False(supervisor.RunningPids.ContainsKey("626labs.a"));  // and untracked
    }

    private sealed class FakeProcessStarter : IPluginProcessStarter
    {
        public List<(string id, string exePath)> Started { get; } = new();
        public List<int> KilledPids { get; } = new();
        public event Action<int>? ProcessExited;
        private int _nextPid = 1000;
        private readonly Dictionary<string, List<int>> _runningUnder = new(StringComparer.OrdinalIgnoreCase);

        public int Start(string id, string exePath)
        {
            Started.Add((id, exePath));
            return _nextPid++;
        }

        public void Kill(int pid) => KilledPids.Add(pid);

        /// <summary>Test seam: declare which PIDs are "running" out of <paramref name="dir"/>.</summary>
        public void SetRunningUnder(string dir, IEnumerable<int> pids) => _runningUnder[dir] = new List<int>(pids);

        /// <summary>
        /// Declared PIDs minus any already killed — so a poll-until-clear loop terminates
        /// once the fake's processes are "dead".
        /// </summary>
        public IReadOnlyList<int> FindRunningUnder(string dirPath)
        {
            if (!_runningUnder.TryGetValue(dirPath, out var pids)) return Array.Empty<int>();
            var live = new List<int>();
            foreach (var pid in pids)
            {
                if (!KilledPids.Contains(pid)) live.Add(pid);
            }
            return live;
        }

        public void RaiseExit(int pid) => ProcessExited?.Invoke(pid);
    }
}
