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

    private static IPluginLaunchInvoker NoOpLauncher() => new FakeLaunchInvoker();

    [Fact]
    public async Task Handshake_AcceptsMatchingContractVersion()
    {
        var registry = new InMemoryRegistry(new[] { MakeInstalled("626labs.test", "host.events.account-launched") });
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher());

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
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher());

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
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher());

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
        var service = new PluginHostService(registry, "1.4.0", "1.0", hostState, accounts, new InProcessPluginEventBus(), NoOpLauncher());

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
        var service = new PluginHostService(registry, "1.4.0", "1.0", hostState, accounts, new InProcessPluginEventBus(), NoOpLauncher());

        var list = await service.GetRunningAccounts(new Empty(), FakeServerCallContext.Create());

        var account = Assert.Single(list.Accounts);
        Assert.Equal("00000000-0000-0000-0000-000000000001", account.AccountId);
        Assert.Equal(12345L, account.RobloxUserId);
        Assert.Equal("Alice", account.DisplayName);
        Assert.Equal(9999, account.ProcessId);
    }

    [Fact]
    public async Task SubscribeAccountLaunched_FansOutEvents_ToSubscriber()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var bus = new InProcessPluginEventBus();
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), bus, NoOpLauncher());

        var writer = new TestStreamWriter<AccountLaunchedEvent>();
        using var cts = new CancellationTokenSource();
        var ctx = FakeServerCallContext.Create("/rororo.plugin.v1.RoRoRoHost/SubscribeAccountLaunched", cts.Token);
        var streamTask = service.SubscribeAccountLaunched(new SubscriptionRequest(), writer, ctx);

        // Give the subscription a tick to attach the event handler.
        await Task.Delay(20);
        bus.RaiseAccountLaunched(new RunningAccountSnapshot(
            "00000000-0000-0000-0000-000000000001", 12345, "Alice", 9999));

        // Wait for the event to flow through the channel.
        await writer.WaitForAtLeastAsync(1, TimeSpan.FromSeconds(2));

        cts.Cancel();
        await streamTask; // should complete cleanly on cancel

        var evt = Assert.Single(writer.Written);
        Assert.Equal("00000000-0000-0000-0000-000000000001", evt.AccountId);
        Assert.Equal(12345L, evt.RobloxUserId);
        Assert.Equal("Alice", evt.DisplayName);
        Assert.Equal(9999, evt.ProcessId);
    }

    [Fact]
    public async Task RequestLaunch_DispatchesToLauncher_AndReturnsResult()
    {
        var fakeLauncher = new FakeLaunchInvoker();
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()),
            "1.4.0", "1.0",
            HostStateOff(),
            NoAccounts(),
            new InProcessPluginEventBus(),
            fakeLauncher);

        var accountId = Guid.NewGuid().ToString();
        var result = await service.RequestLaunch(new LaunchRequest
        {
            AccountId = accountId,
        }, FakeServerCallContext.Create());

        Assert.True(result.Ok);
        Assert.Equal(12345, result.ProcessId);
        Assert.Equal(accountId, Assert.Single(fakeLauncher.Invocations));
    }

    private sealed class FakeLaunchInvoker : IPluginLaunchInvoker
    {
        public List<string> Invocations { get; } = new();
        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId)
        {
            Invocations.Add(accountId);
            return Task.FromResult<(bool, string?, int)>((true, null, 12345));
        }
    }

    private sealed class TestStreamWriter<T> : IServerStreamWriter<T>
    {
        private readonly List<T> _written = new();
        private readonly object _lock = new();
        public IReadOnlyList<T> Written
        {
            get { lock (_lock) return _written.ToList(); }
        }
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message)
        {
            lock (_lock) _written.Add(message);
            return Task.CompletedTask;
        }
        public async Task WaitForAtLeastAsync(int count, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_lock) { if (_written.Count >= count) return; }
                await Task.Delay(10);
            }
            int got;
            lock (_lock) got = _written.Count;
            throw new TimeoutException($"Expected at least {count} written; got {got}.");
        }
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
