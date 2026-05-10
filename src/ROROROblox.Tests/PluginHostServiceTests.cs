using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;

namespace ROROROblox.Tests;

public class PluginHostServiceTests
{
    private static InstalledPlugin MakeInstalled(string id, params string[] caps) => new()
    {
        Manifest = new PluginManifest
        {
            SchemaVersion = 1,
            Id = id,
            Name = id,
            Version = "1.0",
            ContractVersion = "1.0",
            Publisher = "626",
            Description = "x",
            Capabilities = caps,
        },
        InstallDir = "/fake",
        Consent = new ConsentRecord
        {
            PluginId = id,
            GrantedCapabilities = caps,
            AutostartEnabled = false,
        },
    };

    private static IPluginHostStateProvider HostStateOff() => new FakeHostStateProvider("Off");

    private static IRunningAccountsProvider NoAccounts() =>
        new FakeRunningAccountsProvider(Array.Empty<RunningAccountSnapshot>());

    [Fact]
    public async Task Handshake_AcceptsMatchingContractVersion()
    {
        var registry = new InMemoryRegistry(new[] { MakeInstalled("626labs.test", "host.events.account-launched") });
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts());

        var response = await service.Handshake(new HandshakeRequest
        {
            PluginId = "626labs.test",
            ManifestSha256 = "ignored-in-v1",
            ContractVersion = "1.0",
        }, FakeServerCallContext.Create());

        Assert.True(response.Accepted);
        Assert.Equal("1.4.0", response.HostVersion);
    }

    [Fact]
    public async Task Handshake_RejectsContractVersionMismatch()
    {
        var registry = new InMemoryRegistry(new[] { MakeInstalled("626labs.test") });
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts());

        var response = await service.Handshake(new HandshakeRequest
        {
            PluginId = "626labs.test",
            ContractVersion = "99.0",
        }, FakeServerCallContext.Create());

        Assert.False(response.Accepted);
        Assert.Contains("contract", response.RejectReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Handshake_RejectsUnknownPluginId()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts());

        var response = await service.Handshake(new HandshakeRequest
        {
            PluginId = "nonexistent",
            ContractVersion = "1.0",
        }, FakeServerCallContext.Create());

        Assert.False(response.Accepted);
        Assert.Contains("not installed", response.RejectReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetHostInfo_ReturnsCurrentVersionAndState()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var hostState = new FakeHostStateProvider("On");
        var accounts = new FakeRunningAccountsProvider(Array.Empty<RunningAccountSnapshot>());
        var service = new PluginHostService(registry, "1.4.0", "1.0", hostState, accounts);

        var info = await service.GetHostInfo(new Empty(), FakeServerCallContext.Create());

        Assert.Equal("1.4.0", info.Version);
        Assert.True(info.MultiInstanceEnabled);
        Assert.Equal("On", info.MultiInstanceState);
    }

    [Fact]
    public async Task GetRunningAccounts_ReturnsListFromProvider()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var hostState = new FakeHostStateProvider("Off");
        var accounts = new FakeRunningAccountsProvider(new[]
        {
            new RunningAccountSnapshot("00000000-0000-0000-0000-000000000001", 12345, "Alice", 9999),
        });
        var service = new PluginHostService(registry, "1.4.0", "1.0", hostState, accounts);

        var list = await service.GetRunningAccounts(new Empty(), FakeServerCallContext.Create());

        var account = Assert.Single(list.Accounts);
        Assert.Equal("00000000-0000-0000-0000-000000000001", account.AccountId);
        Assert.Equal(12345L, account.RobloxUserId);
        Assert.Equal("Alice", account.DisplayName);
        Assert.Equal(9999, account.ProcessId);
    }

    private sealed class InMemoryRegistry : IInstalledPluginsLookup
    {
        private readonly List<InstalledPlugin> _plugins;
        public InMemoryRegistry(IEnumerable<InstalledPlugin> plugins) { _plugins = plugins.ToList(); }
        public InstalledPlugin? FindById(string id) => _plugins.FirstOrDefault(p => p.Manifest.Id == id);
    }

    private sealed class FakeHostStateProvider : IPluginHostStateProvider
    {
        public FakeHostStateProvider(string state) { MultiInstanceState = state; }
        public string MultiInstanceState { get; }
        public bool MultiInstanceEnabled => MultiInstanceState == "On";
    }

    private sealed class FakeRunningAccountsProvider : IRunningAccountsProvider
    {
        private readonly List<RunningAccountSnapshot> _snapshots;
        public FakeRunningAccountsProvider(IEnumerable<RunningAccountSnapshot> snapshots) { _snapshots = snapshots.ToList(); }
        public IReadOnlyList<RunningAccountSnapshot> Snapshot() => _snapshots;
    }
}
