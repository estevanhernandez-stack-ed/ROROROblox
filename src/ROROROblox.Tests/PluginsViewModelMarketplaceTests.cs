using System.IO;
using System.Net.Http;
using ROROROblox.App.Distribution;
using ROROROblox.App.Plugins;
using ROROROblox.App.Plugins.Adapters;

namespace ROROROblox.Tests;

public class PluginsViewModelMarketplaceTests : IDisposable
{
    private const string PluginId = "626labs.fake";
    private const string ManifestJson =
        """{"schemaVersion":1,"id":"626labs.fake","name":"Fake Plugin","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":["host.events.account-launched"]}""";

    private readonly string _tempRoot;
    private readonly string _pluginsRoot;
    private readonly ConsentStore _consentStore;
    private readonly PluginRegistry _registry;
    private readonly InstalledPluginsLookupAdapter _adapter;
    private readonly PluginInstaller _installer;
    private readonly PluginProcessSupervisor _supervisor;

    public PluginsViewModelMarketplaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ROROROblox-mktplace-{Guid.NewGuid():N}");
        _pluginsRoot = Path.Combine(_tempRoot, "plugins");
        var pluginDir = Path.Combine(_pluginsRoot, PluginId);
        Directory.CreateDirectory(pluginDir);
        File.WriteAllText(Path.Combine(pluginDir, "manifest.json"), ManifestJson);

        _consentStore = new ConsentStore(Path.Combine(_tempRoot, "consent.dat"));
        _registry = new PluginRegistry(_pluginsRoot, _consentStore);
        _adapter = new InstalledPluginsLookupAdapter(_registry);
        _installer = new PluginInstaller(new HttpClient(), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 8, 0, 0));
        _supervisor = new PluginProcessSupervisor(new FakeStarter());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private const string CatalogWithNewerFake =
        """
        [{"id":"626labs.fake","name":"Fake Plugin","description":"d","publisher":"626 Labs","latestVersion":"0.2.0","installUrl":"https://github.com/x/fake/releases/latest/download/"},
        {"id":"626labs.other","name":"Other","description":"d","publisher":"626 Labs","latestVersion":"1.0.0","installUrl":"https://github.com/x/other/releases/latest/download/"}]
        """;

    private PluginsViewModel BuildVm(bool isPackaged) => new(
        _registry, _adapter, _consentStore, _installer, _supervisor,
        _ => Task.FromResult<IReadOnlyList<string>?>(Array.Empty<string>()),
        new FakeDistributionMode(isPackaged),
        new PluginCatalogClient(_ => Task.FromResult(CatalogWithNewerFake)),
        new Version(1, 8, 0, 0));

    [Fact]
    public async Task Unpackaged_CatalogDrivesAvailableAndUpdateBadge()
    {
        var vm = BuildVm(isPackaged: false);

        await vm.LoadAsync();

        Assert.True(vm.MarketplaceEnabled);
        // The installed fake has a newer catalog version → update badge.
        var installedRow = Assert.Single(vm.Plugins);
        Assert.True(installedRow.UpdateAvailable);
        Assert.Contains("0.2.0", installedRow.UpdateLabel);
        // The other catalog entry is not installed → Available.
        var avail = Assert.Single(vm.Available);
        Assert.Equal("626labs.other", avail.Id);
    }

    [Fact]
    public async Task Packaged_NoCatalogFetch_NoAvailable_NoBadges()
    {
        var vm = BuildVm(isPackaged: true);

        await vm.LoadAsync();

        Assert.False(vm.MarketplaceEnabled);
        Assert.Empty(vm.Available);
        Assert.False(Assert.Single(vm.Plugins).UpdateAvailable); // no update state applied when packaged
    }

    private sealed class FakeDistributionMode(bool packaged) : IDistributionMode
    {
        public bool IsPackaged => packaged;
    }

    private sealed class FakeStarter : IPluginProcessStarter
    {
        public List<(string PluginId, string ExePath)> Started { get; } = new();
        public event Action<int>? ProcessExited;
        public int Start(string pluginId, string exePath) { Started.Add((pluginId, exePath)); return 1; }
        public void Kill(int pid) => ProcessExited?.Invoke(pid);
        public IReadOnlyList<int> FindRunningUnder(string dirPath) => Array.Empty<int>();
    }
}
