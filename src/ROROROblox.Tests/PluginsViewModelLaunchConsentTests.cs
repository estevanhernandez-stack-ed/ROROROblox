using System.IO;
using System.Net.Http;
using ROROROblox.App.Distribution;
using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;

namespace ROROROblox.Tests;

/// <summary>
/// Launch-path consent gate: a plugin that has NO consent record (dev-dropped —
/// files copied into the plugins root by hand, so the install flow's consent
/// sheet never ran) must get the sheet on first Launch. A cancelled sheet
/// aborts the launch and writes nothing; an existing record (even one with
/// zero grants) never re-prompts.
/// </summary>
public class PluginsViewModelLaunchConsentTests : IDisposable
{
    private const string PluginId = "626labs.fake";
    private const string ManifestJson =
        """{"schemaVersion":1,"id":"626labs.fake","name":"Fake Plugin","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":["host.events.account-launched","host.queries.account-activity"]}""";

    private readonly string _tempRoot;
    private readonly string _pluginsRoot;
    private readonly ConsentStore _consentStore;
    private readonly PluginRegistry _registry;
    private readonly InstalledPluginsLookupAdapter _adapter;
    private readonly PluginInstaller _installer;
    private readonly FakeStarter _starter;
    private readonly PluginProcessSupervisor _supervisor;

    public PluginsViewModelLaunchConsentTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ROROROblox-launchconsent-{Guid.NewGuid():N}");
        _pluginsRoot = Path.Combine(_tempRoot, "plugins");
        var pluginDir = Path.Combine(_pluginsRoot, PluginId);
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"), ManifestJson);

        _consentStore = new ConsentStore(Path.Combine(_tempRoot, "consent.dat"));
        _registry = new PluginRegistry(_pluginsRoot, _consentStore);
        _adapter = new InstalledPluginsLookupAdapter(_registry);
        _installer = new PluginInstaller(new HttpClient(), _pluginsRoot,
            (_, _) => Task.CompletedTask, new Version(1, 8, 0, 0));
        _starter = new FakeStarter();
        _supervisor = new PluginProcessSupervisor(_starter);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private PluginsViewModel BuildVm(
        Func<PluginManifest, Task<IReadOnlyList<string>?>> showSheet,
        CapturingLogger<PluginsViewModel>? log = null)
        => new(_registry, _adapter, _consentStore, _installer, _supervisor, showSheet,
            new FakeDistributionMode(isPackaged: false),
            new PluginCatalogClient(_ => Task.FromResult("[]")),
            new Version(1, 8, 0, 0),
            log);

    [Fact]
    public async Task Launch_WithoutConsentRecord_ShowsSheetPersistsGrantAndStarts()
    {
        var sheetShown = 0;
        var vm = BuildVm(manifest =>
        {
            sheetShown++;
            Assert.Equal(PluginId, manifest.Id);
            return Task.FromResult<IReadOnlyList<string>?>(manifest.Capabilities.ToList());
        });
        await vm.LoadAsync();

        await vm.LaunchPluginAsync(vm.Plugins.Single());

        Assert.Equal(1, sheetShown);
        var record = (await _consentStore.ListAsync()).SingleOrDefault(r => r.PluginId == PluginId);
        Assert.NotNull(record);
        Assert.Contains("host.queries.account-activity", record!.GrantedCapabilities);
        Assert.Single(_starter.Started);
    }

    [Fact]
    public async Task Launch_SheetCancelled_DoesNotStartAndWritesNothing()
    {
        var vm = BuildVm(_ => Task.FromResult<IReadOnlyList<string>?>(null));
        await vm.LoadAsync();

        await vm.LaunchPluginAsync(vm.Plugins.Single());

        Assert.Empty(_starter.Started);
        Assert.Empty(await _consentStore.ListAsync());
        Assert.False(vm.Plugins.Single().IsRunning);
    }

    [Fact]
    public async Task Launch_WithExistingConsentRecord_NeverShowsSheet()
    {
        // Even a zero-capability record is a deliberate user decision — no re-prompt.
        await _consentStore.GrantAsync(PluginId, Array.Empty<string>());
        _adapter.Refresh();

        var sheetShown = 0;
        var vm = BuildVm(_ =>
        {
            sheetShown++;
            return Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>());
        });
        await vm.LoadAsync();

        await vm.LaunchPluginAsync(vm.Plugins.Single());

        Assert.Equal(0, sheetShown);
        Assert.Single(_starter.Started);
    }

    [Fact]
    public async Task Launch_FirstLaunchGrant_LogsConsentOutcome()
    {
        // Issue #36 residual: consent decisions must leave durable log evidence — the banner
        // strings vanish on window close. Grant path logs granted/declared counts + the ids.
        var log = new CapturingLogger<PluginsViewModel>();
        var vm = BuildVm(
            manifest => Task.FromResult<IReadOnlyList<string>?>(new[] { "host.events.account-launched" }),
            log);
        await vm.LoadAsync();

        await vm.LaunchPluginAsync(vm.Plugins.Single());

        var line = log.Snapshot().Single(l => l.Contains("Plugin consent: granted"));
        Assert.Contains("1/2 capabilities", line);
        Assert.Contains(PluginId, line);
        Assert.Contains("host.events.account-launched", line);
    }

    [Fact]
    public async Task Launch_SheetCancelled_LogsCancelledOutcome()
    {
        var log = new CapturingLogger<PluginsViewModel>();
        var vm = BuildVm(_ => Task.FromResult<IReadOnlyList<string>?>(null), log);
        await vm.LoadAsync();

        await vm.LaunchPluginAsync(vm.Plugins.Single());

        var line = log.Snapshot().Single(l => l.Contains("Plugin consent: sheet cancelled"));
        Assert.Contains(PluginId, line);
        Assert.Contains("first Launch", line);
    }

    [Fact]
    public async Task Revoke_LogsConsentRevocation()
    {
        // Revocation is the OTHER half of the consent-evidence story — a grant with no matching
        // revocation record reads as still-consented in any audit. Pin the revoke log line so a
        // merge that drops it goes red instead of silently half-breaking the #36 guarantee.
        await _consentStore.GrantAsync(PluginId, Array.Empty<string>());
        _adapter.Refresh();
        var log = new CapturingLogger<PluginsViewModel>();
        var vm = BuildVm(_ => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>()), log);
        await vm.LoadAsync();

        await vm.RevokeAsync(vm.Plugins.Single());

        var line = log.Snapshot().Single(l => l.Contains("Plugin consent: revoked"));
        Assert.Contains(PluginId, line);
    }

    [Fact]
    public async Task ToggleAutostart_LogsStateChange()
    {
        await _consentStore.GrantAsync(PluginId, Array.Empty<string>());
        _adapter.Refresh();
        var log = new CapturingLogger<PluginsViewModel>();
        var vm = BuildVm(_ => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>()), log);
        await vm.LoadAsync();

        await vm.ToggleAutostartAsync(vm.Plugins.Single());

        var line = log.Snapshot().Single(l => l.Contains("autostart"));
        Assert.Contains(PluginId, line);
    }

    private sealed class FakeStarter : IPluginProcessStarter
    {
        private int _nextPid = 4242;
        public List<(string PluginId, string ExePath)> Started { get; } = new();
        public event Action<int>? ProcessExited;

        public int Start(string pluginId, string exePath)
        {
            Started.Add((pluginId, exePath));
            return _nextPid++;
        }

        public void Kill(int pid) => ProcessExited?.Invoke(pid);

        public IReadOnlyList<int> FindRunningUnder(string dirPath) => Array.Empty<int>();
    }

    // Deterministic stand-in for Win32DistributionMode — these tests exercise the launch/consent
    // path, not the marketplace gate, so always-unpackaged keeps them independent of the real
    // package-identity probe (which would otherwise vary by how the test host itself is running).
    private sealed class FakeDistributionMode(bool isPackaged) : IDistributionMode
    {
        public bool IsPackaged => isPackaged;
    }
}
