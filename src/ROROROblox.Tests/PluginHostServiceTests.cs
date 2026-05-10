using Grpc.Core;
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

    /// <summary>
    /// Minimal ServerCallContext fake. Grpc.Core.Testing.TestServerCallContext does exist
    /// in the Grpc.Core.Testing package, but pulling that whole package in just for a
    /// no-op context is heavier than a 20-line stub. The host RPCs do not read any context
    /// fields in v1, so a no-op is sufficient.
    /// </summary>
    private sealed class FakeServerCallContext : ServerCallContext
    {
        public static FakeServerCallContext Create() => new();

        protected override string MethodCore => "Test";
        protected override string HostCore => "test";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.UtcNow.AddMinutes(1);
        protected override Metadata RequestHeadersCore => new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new("anonymous", new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options)
            => throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
