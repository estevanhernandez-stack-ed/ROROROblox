using System.IO;
using System.Net.Http;
using ROROROblox.App.Distribution;
using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;

namespace ROROROblox.Tests;

/// <summary>
/// The plugin health line and banner (#242). The field log had every plugin dying with 0x80008085
/// before reaching its own code, the Restart button pressed 13 times in two seconds, and one install
/// reporting a failed start for a plugin whose exit was simply lost. These pin what the user is told.
/// </summary>
public class PluginHealthTests : IDisposable
{
    // ---- Classification ------------------------------------------------------------------

    [Theory]
    [InlineData(unchecked((int)0x80008085), 0.1, PluginExitKind.CouldNotStart)]
    [InlineData(unchecked((int)0x80008085), 600, PluginExitKind.CouldNotStart)]   // the code decides, not the timing
    [InlineData(unchecked((int)0x80008080), 0.1, PluginExitKind.CouldNotStart)]   // range start
    [InlineData(unchecked((int)0x800080A2), 0.1, PluginExitKind.CouldNotStart)]   // range end
    [InlineData(unchecked((int)0x800080A3), 0.1, PluginExitKind.CrashedWhileStarting)]
    [InlineData(unchecked((int)0xE0434352), 1, PluginExitKind.CrashedWhileStarting)] // managed unhandled exception
    [InlineData(0, 1, PluginExitKind.ClosedAtStartup)]
    [InlineData(1, 600, PluginExitKind.Crashed)]
    [InlineData(0, 600, PluginExitKind.Stopped)]
    public void ExitsAreClassifiedByCodeThenUptime(int code, double uptimeSeconds, PluginExitKind expected)
    {
        Assert.Equal(expected, new PluginProcessExit(42, code, TimeSpan.FromSeconds(uptimeSeconds)).Kind);
    }

    [Fact]
    public void AnUnreadableCodeIsTreatedAsACrashNotAStop()
    {
        // Claiming "stopped" for an exit we could not read would be the comfortable lie.
        Assert.Equal(PluginExitKind.CrashedWhileStarting, new PluginProcessExit(1, null, TimeSpan.FromSeconds(1)).Kind);
        Assert.Equal("?", new PluginProcessExit(1, null, TimeSpan.Zero).CodeHex);
    }

    [Fact]
    public void TheHealthLineCarriesTheCodeInTheLogsForm()
    {
        var line = PluginsViewModel.HealthLineFor(new PluginProcessExit(1, unchecked((int)0x80008085), TimeSpan.Zero));

        Assert.Contains("0x80008085", line);
    }

    // ---- Supervisor: an exit before the mapping is not lost --------------------------------

    [Fact]
    public void AnExitThatArrivesBeforeStartReturnsStillReachesThePage()
    {
        var starter = new DyingStarter { DieWith = unchecked((int)0x80008085) };
        var supervisor = new PluginProcessSupervisor(starter);
        var fired = new List<(string Id, PluginProcessExit Exit)>();
        supervisor.PluginExited += (id, exit) => fired.Add((id, exit));

        supervisor.Start(Plugin("626labs.dying"), PluginLaunchReason.Manual);

        var (id, exit) = Assert.Single(fired);
        Assert.Equal("626labs.dying", id);
        Assert.Equal(PluginExitKind.CouldNotStart, exit.Kind);
        Assert.False(supervisor.RunningPids.ContainsKey("626labs.dying"), "a dead plugin must not read as running");
    }

    [Fact]
    public void AnOldExitWithAReusedPidDoesNotKillANewPlugin()
    {
        // Windows reuses pids. An exit recorded before this start began belongs to another process.
        var starter = new DyingStarter { NextPid = 7 };
        var supervisor = new PluginProcessSupervisor(starter);
        var fired = 0;
        supervisor.PluginExited += (_, _) => fired++;
        starter.RaiseExit(new PluginProcessExit(7, 0, TimeSpan.FromMinutes(5))); // unmapped, so buffered
        Thread.Sleep(20);

        supervisor.Start(Plugin("626labs.fresh"), PluginLaunchReason.Manual);

        Assert.Equal(0, fired);
        Assert.Equal(7, supervisor.RunningPids["626labs.fresh"]);
    }

    // ---- The page ---------------------------------------------------------------------------

    private const string PluginId = "626labs.fake";
    private const string ManifestJson =
        """{"schemaVersion":1,"id":"626labs.fake","name":"Fake Plugin","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[]}""";

    private readonly string _tempRoot;
    private readonly ConsentStore _consentStore;
    private readonly PluginRegistry _registry;
    private readonly InstalledPluginsLookupAdapter _adapter;
    private readonly PluginInstaller _installer;

    public PluginHealthTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ROROROblox-health-{Guid.NewGuid():N}");
        var pluginsRoot = Path.Combine(_tempRoot, "plugins");
        Directory.CreateDirectory(Path.Combine(pluginsRoot, PluginId));
        File.WriteAllText(Path.Combine(pluginsRoot, PluginId, "manifest.json"), ManifestJson);
        _consentStore = new ConsentStore(Path.Combine(_tempRoot, "consent.dat"));
        _registry = new PluginRegistry(pluginsRoot, _consentStore);
        _adapter = new InstalledPluginsLookupAdapter(_registry);
        _installer = new PluginInstaller(new HttpClient(), pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 8, 0, 0));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private async Task<(PluginsViewModel Vm, DyingStarter Starter)> PageAsync(int? dieWith, double uptimeSeconds = 0.1)
    {
        await _consentStore.GrantAsync(PluginId, Array.Empty<string>());
        _adapter.Refresh();
        var starter = new DyingStarter { DieWith = dieWith, Uptime = TimeSpan.FromSeconds(uptimeSeconds) };
        var vm = new PluginsViewModel(_registry, _adapter, _consentStore, _installer, new PluginProcessSupervisor(starter),
            _ => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>()),
            new FakeDistributionMode(isPackaged: false),
            new PluginCatalogClient(_ => Task.FromResult("[]")),
            new Version(1, 8, 0, 0))
        {
            // Inline, as on the UI thread in production; see DispatcherSource for why tests need it.
            DispatcherSource = () => null,
        };
        await vm.LoadAsync();
        return (vm, starter);
    }

    [Fact]
    public async Task APluginThatCannotStart_SaysSoAndOffersNoRestart()
    {
        var (vm, _) = await PageAsync(dieWith: unchecked((int)0x80008085));
        var row = vm.Plugins.Single();

        await vm.LaunchPluginAsync(row);

        Assert.False(row.IsRunning, "a plugin that died at launch must not read as running");
        Assert.Contains("0x80008085", row.HealthLine);
        Assert.False(vm.BannerIsRestartable, "restarting cannot fix a .NET host failure");
        Assert.Contains("Fake Plugin", vm.StatusBanner);
    }

    [Fact]
    public async Task AFirstCrashWhileStarting_StillOffersRestart()
    {
        var (vm, _) = await PageAsync(dieWith: unchecked((int)0xE0434352));
        var row = vm.Plugins.Single();

        await vm.LaunchPluginAsync(row);

        Assert.False(row.IsRunning);
        Assert.Contains("0xE0434352", row.HealthLine);
        Assert.True(vm.BannerIsRestartable, "one crash might be transient");
    }

    [Fact]
    public async Task RepeatedCrashesWhileStarting_StopOfferingRestart()
    {
        var (vm, _) = await PageAsync(dieWith: unchecked((int)0xE0434352));
        var row = vm.Plugins.Single();

        await vm.LaunchPluginAsync(row);
        for (var i = 1; i < PluginsViewModel.StartupFailureLimit; i++)
        {
            Assert.True(vm.BannerIsRestartable);
            vm.RestartCommand.Execute(null);
        }

        Assert.False(vm.BannerIsRestartable, "the field log had Restart pressed 13 times in two seconds");
        Assert.False(row.IsRunning);
    }

    [Fact]
    public async Task ARestartThatDiesAtOnce_KeepsTheExitBannerRatherThanSayingRestarted()
    {
        var (vm, _) = await PageAsync(dieWith: unchecked((int)0xE0434352));
        var row = vm.Plugins.Single();
        await vm.LaunchPluginAsync(row);
        var exitBanner = vm.StatusBanner;

        vm.RestartCommand.Execute(null);

        Assert.Equal(exitBanner, vm.StatusBanner);
        Assert.True(vm.BannerIsRestartable, "second failure, still under the limit");
    }

    [Fact]
    public async Task AHealthyLaunch_ClearsTheLastExitsLine()
    {
        var (vm, starter) = await PageAsync(dieWith: unchecked((int)0xE0434352));
        var row = vm.Plugins.Single();
        await vm.LaunchPluginAsync(row);
        Assert.NotEqual(string.Empty, row.HealthLine);

        starter.DieWith = null; // fixed
        vm.RestartCommand.Execute(null);

        Assert.True(row.IsRunning);
        Assert.Equal(string.Empty, row.HealthLine);
    }

    // ---- Helpers ----------------------------------------------------------------------------

    private static InstalledPlugin Plugin(string id) => new()
    {
        Manifest = new PluginManifest
        {
            SchemaVersion = 1, Id = id, Name = id, Version = "1.0", ContractVersion = "1.0",
            Publisher = "626", Description = "x", Capabilities = Array.Empty<string>(),
        },
        InstallDir = "/fake",
        Consent = new ConsentRecord { PluginId = id, GrantedCapabilities = Array.Empty<string>() },
    };

    /// <summary>
    /// A starter whose plugin can die before Start returns, the field shape: the exit is raised
    /// inside Start, exactly as a real Exited handler does once Start opens its gate.
    /// </summary>
    private sealed class DyingStarter : IPluginProcessStarter
    {
        public int? DieWith { get; set; }
        public TimeSpan Uptime { get; set; } = TimeSpan.FromSeconds(0.1);
        public int NextPid { get; set; } = 5000;
        public event Action<PluginProcessExit>? ProcessExited;

        public int Start(string pluginId, string exePath, PluginLaunchReason reason)
        {
            var pid = NextPid++;
            if (DieWith is { } code) ProcessExited?.Invoke(new PluginProcessExit(pid, code, Uptime));
            return pid;
        }

        public void RaiseExit(PluginProcessExit exit) => ProcessExited?.Invoke(exit);
        public void Kill(int pid) { }
        public IReadOnlyList<int> FindRunningUnder(string dirPath) => Array.Empty<int>();
    }

    private sealed class FakeDistributionMode(bool isPackaged) : IDistributionMode
    {
        public bool IsPackaged => isPackaged;
    }
}
