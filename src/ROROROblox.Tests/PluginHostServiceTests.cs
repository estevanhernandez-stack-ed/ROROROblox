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

    private static PluginUITranslator NoUITranslator() => new(new FakeUIHost());

    private static IActivitySnapshotProvider NoActivity() =>
        new FakeActivitySnapshotProvider(Array.Empty<AccountActivitySnapshot>());

    private static IAccountActivityMarker NoActivityMarker() => new FakeActivityMarker();

    private static IPluginAccountStopper NoStopper() => new FakeAccountStopper();

    [Fact]
    public async Task Handshake_AcceptsMatchingContractVersion()
    {
        var registry = new InMemoryRegistry(new[] { MakeInstalled("626labs.test", "host.events.account-launched") });
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

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
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

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
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

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
        var service = new PluginHostService(registry, "1.4.0", "1.0", hostState, accounts, new InProcessPluginEventBus(), NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

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
            new RunningAccountSnapshot("00000000-0000-0000-0000-000000000001", 12345, "Alice", 9999,
                PlaceId: 606849621, PlaceName: "Pet Simulator"),
        });
        var service = new PluginHostService(registry, "1.4.0", "1.0", hostState, accounts, new InProcessPluginEventBus(), NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var list = await service.GetRunningAccounts(new Empty(), FakeServerCallContext.Create());

        var account = Assert.Single(list.Accounts);
        Assert.Equal("00000000-0000-0000-0000-000000000001", account.AccountId);
        Assert.Equal(12345L, account.RobloxUserId);
        Assert.Equal("Alice", account.DisplayName);
        Assert.Equal(9999, account.ProcessId);
        Assert.Equal(606849621L, account.PlaceId);
        Assert.Equal("Pet Simulator", account.PlaceName);
    }

    [Fact]
    public async Task GetRunningAccounts_NoGameInfo_DefaultsToZeroAndEmpty()
    {
        // Presence can lag a fresh launch — snapshots built before the presence
        // consumer fills CurrentPlaceId must surface as 0/"" (proto defaults),
        // which plugins treat as "no game info".
        var accounts = new FakeRunningAccountsProvider(new[]
        {
            new RunningAccountSnapshot("00000000-0000-0000-0000-000000000002", 777, "Bob", 1234),
        });
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            new FakeHostStateProvider("Off"), accounts, new InProcessPluginEventBus(),
            NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var list = await service.GetRunningAccounts(new Empty(), FakeServerCallContext.Create());

        var account = Assert.Single(list.Accounts);
        Assert.Equal(0L, account.PlaceId);
        Assert.Equal(string.Empty, account.PlaceName);
    }

    [Fact]
    public async Task SubscribeAccountLaunched_FansOutEvents_ToSubscriber()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var bus = new InProcessPluginEventBus();
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), bus, NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var writer = new TestStreamWriter<AccountLaunchedEvent>();
        using var cts = new CancellationTokenSource();
        var ctx = FakeServerCallContext.Create("/rororo.plugin.v1.RoRoRoHost/SubscribeAccountLaunched", cts.Token);
        var streamTask = service.SubscribeAccountLaunched(new SubscriptionRequest(), writer, ctx);

        // Give the subscription a tick to attach the event handler.
        await Task.Delay(20);
        bus.RaiseAccountLaunched(new RunningAccountSnapshot(
            "00000000-0000-0000-0000-000000000001", 12345, "Alice", 9999,
            PlaceId: 606849621, PlaceName: "Pet Simulator"));

        // Wait for the event to flow through the channel.
        await writer.WaitForAtLeastAsync(1, TimeSpan.FromSeconds(2));

        cts.Cancel();
        await streamTask; // should complete cleanly on cancel

        var evt = Assert.Single(writer.Written);
        Assert.Equal("00000000-0000-0000-0000-000000000001", evt.AccountId);
        Assert.Equal(12345L, evt.RobloxUserId);
        Assert.Equal("Alice", evt.DisplayName);
        Assert.Equal(9999, evt.ProcessId);
        Assert.Equal(606849621L, evt.PlaceId);
        Assert.Equal("Pet Simulator", evt.PlaceName);
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
            fakeLauncher,
            NoUITranslator(),
            NoActivity(), NoActivityMarker(), NoStopper());

        var accountId = Guid.NewGuid().ToString();
        var result = await service.RequestLaunch(new LaunchRequest
        {
            AccountId = accountId,
        }, FakeServerCallContext.Create());

        Assert.True(result.Ok);
        Assert.Equal(12345, result.ProcessId);
        Assert.Equal(accountId, Assert.Single(fakeLauncher.Invocations));
    }

    [Fact]
    public async Task RequestLaunchTarget_DispatchesShareUrl_ToLauncher()
    {
        var fake = new FakeLaunchInvoker();
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), fake, NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var acct = Guid.NewGuid().ToString();
        var result = await service.RequestLaunchTarget(new LaunchTargetRequest
        {
            AccountId = acct,
            ShareUrl = "https://www.roblox.com/games/1?privateServerLinkCode=ABC",
        }, FakeServerCallContext.Create());

        Assert.True(result.Ok);
        Assert.Equal(6789, result.ProcessId);
        var inv = Assert.Single(fake.TargetInvocations);
        Assert.Equal(acct, inv.acct);
        Assert.Equal("https://www.roblox.com/games/1?privateServerLinkCode=ABC", inv.url);
        Assert.Null(inv.follow);
    }

    [Fact]
    public async Task RequestLaunchTarget_DispatchesFollowUserId_ToLauncher()
    {
        var fake = new FakeLaunchInvoker();
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), fake, NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var acct = Guid.NewGuid().ToString();
        var result = await service.RequestLaunchTarget(new LaunchTargetRequest
        {
            AccountId = acct,
            FollowUserId = 4242L,
        }, FakeServerCallContext.Create());

        Assert.True(result.Ok);
        var inv = Assert.Single(fake.TargetInvocations);
        Assert.Null(inv.url);
        Assert.Equal(4242L, inv.follow);
    }

    [Fact]
    public async Task GetCurrentServer_ReturnsPresentFalse_WhenNone()
    {
        var fake = new FakeLaunchInvoker { Current = null };
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), fake, NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var result = await service.GetCurrentServer(new Empty(), FakeServerCallContext.Create());
        Assert.False(result.Present);
    }

    [Fact]
    public async Task GetCurrentServer_MapsInfo_WhenPresent()
    {
        var fake = new FakeLaunchInvoker { Current = new CurrentServerInfo("https://x", "Pet Sim", 99, 1700000000000) };
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), fake, NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var result = await service.GetCurrentServer(new Empty(), FakeServerCallContext.Create());
        Assert.True(result.Present);
        Assert.Equal("https://x", result.ShareUrl);
        Assert.Equal("Pet Sim", result.PlaceName);
        Assert.Equal(99, result.PlaceId);
        Assert.Equal(1700000000000, result.LastLaunchedAtUnixMs);
    }

    // =====================================================================
    // UpdateUI / RemoveUI ownership refusal (verify run f7ebdbd1 finding:
    // both accepted a bogus handle from an unconsented caller). Ownership is
    // the ONLY gate on these two RPCs — the capability map deliberately
    // leaves them ungated — so an unknown or foreign handle must refuse with
    // PermissionDenied, and the same status for both cases so callers can't
    // probe which handle ids exist.
    // =====================================================================

    private static PluginHostService ServiceWithUI(PluginUITranslator translator) => new(
        new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
        HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher(),
        translator, NoActivity(), NoActivityMarker(), NoStopper());

    [Fact]
    public async Task UpdateUI_UnknownHandle_ThrowsPermissionDenied()
    {
        var service = ServiceWithUI(new PluginUITranslator(new SequencedUIHost()));

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.UpdateUI(new UIUpdate
        {
            Handle = new UIHandle { Id = "never-issued" },
            MenuItem = new MenuItemSpec { Label = "x" },
        }, FakeServerCallContext.CreateForPlugin("626labs.a")));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateUI_ForeignHandle_ThrowsPermissionDenied()
    {
        var translator = new PluginUITranslator(new SequencedUIHost());
        var service = ServiceWithUI(translator);
        var handle = await service.AddTrayMenuItem(
            new MenuItemSpec { Label = "mine" }, FakeServerCallContext.CreateForPlugin("626labs.a"));

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.UpdateUI(new UIUpdate
        {
            Handle = handle,
            MenuItem = new MenuItemSpec { Label = "hijacked" },
        }, FakeServerCallContext.CreateForPlugin("626labs.b")));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateUI_MissingHeader_ThrowsPermissionDenied()
    {
        var translator = new PluginUITranslator(new SequencedUIHost());
        var service = ServiceWithUI(translator);
        var handle = await service.AddTrayMenuItem(
            new MenuItemSpec { Label = "mine" }, FakeServerCallContext.CreateForPlugin("626labs.a"));

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.UpdateUI(new UIUpdate
        {
            Handle = handle,
            MenuItem = new MenuItemSpec { Label = "x" },
        }, FakeServerCallContext.Create())); // no x-plugin-id header

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateUI_NoHandle_ThrowsPermissionDenied()
    {
        var service = ServiceWithUI(new PluginUITranslator(new SequencedUIHost()));

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.UpdateUI(
            new UIUpdate { MenuItem = new MenuItemSpec { Label = "x" } }, // Handle never set
            FakeServerCallContext.CreateForPlugin("626labs.a")));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Fact]
    public async Task UpdateUI_OwnedHandle_Succeeds()
    {
        var translator = new PluginUITranslator(new SequencedUIHost());
        var service = ServiceWithUI(translator);
        var handle = await service.AddTrayMenuItem(
            new MenuItemSpec { Label = "mine" }, FakeServerCallContext.CreateForPlugin("626labs.a"));

        var result = await service.UpdateUI(new UIUpdate
        {
            Handle = handle,
            MenuItem = new MenuItemSpec { Label = "renamed" },
        }, FakeServerCallContext.CreateForPlugin("626labs.a"));

        Assert.NotNull(result);
    }

    [Fact]
    public async Task RemoveUI_UnknownHandle_ThrowsPermissionDenied()
    {
        var host = new SequencedUIHost();
        var service = ServiceWithUI(new PluginUITranslator(host));

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.RemoveUI(
            new UIHandle { Id = "never-issued" }, FakeServerCallContext.CreateForPlugin("626labs.a")));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Empty(host.Removed);
    }

    [Fact]
    public async Task RemoveUI_ForeignHandle_ThrowsPermissionDenied_AndRemovesNothing()
    {
        var host = new SequencedUIHost();
        var translator = new PluginUITranslator(host);
        var service = ServiceWithUI(translator);
        var handle = await service.AddTrayMenuItem(
            new MenuItemSpec { Label = "mine" }, FakeServerCallContext.CreateForPlugin("626labs.a"));

        var ex = await Assert.ThrowsAsync<RpcException>(() => service.RemoveUI(
            handle, FakeServerCallContext.CreateForPlugin("626labs.b")));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        Assert.Empty(host.Removed);
    }

    [Fact]
    public async Task RemoveUI_OwnedHandle_RemovesOnHost()
    {
        var host = new SequencedUIHost();
        var translator = new PluginUITranslator(host);
        var service = ServiceWithUI(translator);
        var handle = await service.AddTrayMenuItem(
            new MenuItemSpec { Label = "mine" }, FakeServerCallContext.CreateForPlugin("626labs.a"));

        await service.RemoveUI(handle, FakeServerCallContext.CreateForPlugin("626labs.a"));

        Assert.Equal(handle.Id, Assert.Single(host.Removed));
    }

    // =====================================================================
    // StopAccounts (agent-ops surface). Empty request means every tracked
    // account; an untracked id reports as failed rather than failing the batch.
    // =====================================================================

    private static PluginHostService ServiceWithStopper(IPluginAccountStopper stopper) => new(
        new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
        HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher(),
        NoUITranslator(), NoActivity(), NoActivityMarker(), stopper);

    [Fact]
    public async Task StopAccounts_EmptyRequest_StopsEveryTrackedAccount()
    {
        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();
        var stopper = new FakeAccountStopper { Tracked = { a, b } };
        var service = ServiceWithStopper(stopper);

        var result = await service.StopAccounts(new StopAccountsRequest(), FakeServerCallContext.Create());

        Assert.Equal(2, result.StoppedCount);
        Assert.Empty(result.FailedAccountIds);
        // TrackedAccountIds order is unspecified (backed by a set) — compare as a set.
        Assert.Equal(new HashSet<string> { a, b }, stopper.Stopped.ToHashSet());
    }

    [Fact]
    public async Task StopAccounts_ExplicitIds_StopsOnlyThose()
    {
        var a = Guid.NewGuid().ToString();
        var b = Guid.NewGuid().ToString();
        var stopper = new FakeAccountStopper { Tracked = { a, b } };
        var service = ServiceWithStopper(stopper);

        var request = new StopAccountsRequest();
        request.AccountIds.Add(a);
        var result = await service.StopAccounts(request, FakeServerCallContext.Create());

        Assert.Equal(1, result.StoppedCount);
        Assert.Equal(a, Assert.Single(stopper.Stopped));
    }

    [Fact]
    public async Task StopAccounts_UntrackedId_ReportsFailedWithoutFailingBatch()
    {
        var tracked = Guid.NewGuid().ToString();
        var untracked = Guid.NewGuid().ToString();
        var stopper = new FakeAccountStopper { Tracked = { tracked } };
        var service = ServiceWithStopper(stopper);

        var request = new StopAccountsRequest();
        request.AccountIds.Add(untracked);
        request.AccountIds.Add(tracked);
        var result = await service.StopAccounts(request, FakeServerCallContext.Create());

        Assert.Equal(1, result.StoppedCount);
        Assert.Equal(untracked, Assert.Single(result.FailedAccountIds));
        Assert.Equal(tracked, Assert.Single(stopper.Stopped));
    }

    [Fact]
    public async Task StopAccounts_DuplicateIds_StopOnce()
    {
        var a = Guid.NewGuid().ToString();
        var stopper = new FakeAccountStopper { Tracked = { a } };
        var service = ServiceWithStopper(stopper);

        var request = new StopAccountsRequest();
        request.AccountIds.Add(a);
        request.AccountIds.Add(a);
        var result = await service.StopAccounts(request, FakeServerCallContext.Create());

        Assert.Equal(1, result.StoppedCount);
        Assert.Single(stopper.Stopped);
    }

    [Fact]
    public async Task StopAccounts_NothingTracked_IsNoOp()
    {
        var stopper = new FakeAccountStopper();
        var service = ServiceWithStopper(stopper);

        var result = await service.StopAccounts(new StopAccountsRequest(), FakeServerCallContext.Create());

        Assert.Equal(0, result.StoppedCount);
        Assert.Empty(result.FailedAccountIds);
        Assert.Empty(stopper.Stopped);
    }

    private sealed class FakeAccountStopper : IPluginAccountStopper
    {
        public HashSet<string> Tracked { get; } = new(StringComparer.Ordinal);
        public List<string> Stopped { get; } = new();
        public IReadOnlyList<string> TrackedAccountIds => Tracked.ToList();
        public bool StopAccount(string accountId)
        {
            if (!Tracked.Contains(accountId)) return false;
            Stopped.Add(accountId);
            return true;
        }
    }

    /// <summary>UI host fake that issues real, unique handle ids (FakeUIHost returns
    /// string.Empty for every handle, which would collide in the ownership map).</summary>
    private sealed class SequencedUIHost : IPluginUIHost
    {
        public List<string> Removed { get; } = new();
        private int _next = 1;
        public string AddTrayMenuItem(string p, string l, string? t, bool e, Action c) => $"handle-{_next++}";
        public string AddRowBadge(string p, string t, string? c, string? tt) => $"handle-{_next++}";
        public string AddStatusPanel(string p, string t, string b) => $"handle-{_next++}";
        public void Update(string h, string l) { }
        public void Remove(string h) => Removed.Add(h);
    }

    private sealed class FakeLaunchInvoker : IPluginLaunchInvoker
    {
        public List<string> Invocations { get; } = new();
        public List<(string acct, string? url, long? follow)> TargetInvocations { get; } = new();
        public CurrentServerInfo? Current { get; set; }

        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId)
        {
            Invocations.Add(accountId);
            return Task.FromResult<(bool, string?, int)>((true, null, 12345));
        }

        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchTargetAsync(string accountId, string? shareUrl, long? followUserId)
        {
            TargetInvocations.Add((accountId, shareUrl, followUserId));
            return Task.FromResult<(bool, string?, int)>((true, null, 6789));
        }

        public Task<CurrentServerInfo?> GetCurrentServerAsync() => Task.FromResult(Current);
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

    private sealed class FakeActivitySnapshotProvider : IActivitySnapshotProvider
    {
        private readonly List<AccountActivitySnapshot> _snapshots;
        public FakeActivitySnapshotProvider(IEnumerable<AccountActivitySnapshot> snapshots) { _snapshots = snapshots.ToList(); }
        public IReadOnlyList<AccountActivitySnapshot> Snapshot() => _snapshots;
    }

    private sealed class FakeActivityMarker : IAccountActivityMarker
    {
        public List<string> MarkedAccountIds { get; } = new();
        public void Mark(string accountId) => MarkedAccountIds.Add(accountId);
    }

    private sealed class FakeUIHost : IPluginUIHost
    {
        public string AddTrayMenuItem(string p, string l, string? t, bool e, Action c) => string.Empty;
        public string AddRowBadge(string p, string t, string? c, string? tt) => string.Empty;
        public string AddStatusPanel(string p, string t, string b) => string.Empty;
        public void Update(string h, string l) { }
        public void Remove(string h) { }
    }
}
