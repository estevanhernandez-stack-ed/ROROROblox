using Grpc.Core;
using ROROROblox.App.Plugins;
using ROROROblox.Core.Diagnostics;
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

    private sealed class FakeSavedAccountsProvider : ISavedAccountsProvider
    {
        private readonly List<SavedAccountSnapshot> _snapshots;
        public FakeSavedAccountsProvider(IEnumerable<SavedAccountSnapshot> snapshots) => _snapshots = snapshots.ToList();
        public IReadOnlyList<SavedAccountSnapshot> Snapshot() => _snapshots;
    }

    [Fact]
    public async Task GetAccounts_ReturnsSavedListFromProvider()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var saved = new FakeSavedAccountsProvider(new[]
        {
            new SavedAccountSnapshot("00000000-0000-0000-0000-000000000001", 12345, "Pokey", IsMain: true),
            new SavedAccountSnapshot("00000000-0000-0000-0000-000000000002", 0, "Spud", IsMain: false),
        });
        var service = new PluginHostService(
            registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(),
            NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper(),
            savedAccounts: saved);

        var list = await service.GetAccounts(new Empty(), FakeServerCallContext.Create());

        Assert.Equal(2, list.Accounts.Count);
        var main = Assert.Single(list.Accounts, a => a.IsMain);
        Assert.Equal("Pokey", main.DisplayName);
        Assert.Equal(12345L, main.RobloxUserId);
        Assert.Equal("00000000-0000-0000-0000-000000000001", main.AccountId);
        var spud = Assert.Single(list.Accounts, a => a.DisplayName == "Spud");
        Assert.Equal(0L, spud.RobloxUserId);
        Assert.False(spud.IsMain);
    }

    [Fact]
    public async Task GetAccounts_WithoutAProvider_FailsPreconditionRatherThanAnsweringEmpty()
    {
        // An unwired provider and an empty roster are different claims — same distinction
        // GetTheme draws for its optional source. SavedAccountsWiringTests pins production.
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var service = new PluginHostService(
            registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(),
            NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => service.GetAccounts(new Empty(), FakeServerCallContext.Create()));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

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
    public async Task GetHostInfo_ReportsBuildIdentity()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var info = await service.GetHostInfo(new Empty(), FakeServerCallContext.Create());

        Assert.Equal("dev", info.InstallFlavour);
        Assert.False(string.IsNullOrWhiteSpace(info.ProcessPath));
        Assert.False(string.IsNullOrWhiteSpace(info.InformationalVersion));
    }

    private sealed class FakeLastExit : ILastExitSource
    {
        public FakeLastExit(LastExit previous) => Previous = previous;
        public LastExit Previous { get; }
    }

    [Fact]
    public async Task GetHostInfo_ReportsLastExitFromProvider()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var at = DateTimeOffset.FromUnixTimeMilliseconds(1_761_350_400_123);
        var lastExit = new FakeLastExit(new LastExit(LastExitRecorder.Crash, "System.X 0x80263001 boom", at));
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper(), lastExit: lastExit);

        var info = await service.GetHostInfo(new Empty(), FakeServerCallContext.Create());

        Assert.Equal("crash", info.LastExitReason);
        Assert.Equal("System.X 0x80263001 boom", info.LastExitDetail);
        Assert.Equal(1_761_350_400_123L, info.LastExitAtUnixMs);
    }

    [Fact]
    public async Task GetHostInfo_NoLastExitProvider_ReportsNone()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var info = await service.GetHostInfo(new Empty(), FakeServerCallContext.Create());

        Assert.Equal("none", info.LastExitReason);
        Assert.Equal(string.Empty, info.LastExitDetail);
        Assert.Equal(0L, info.LastExitAtUnixMs);
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
        await writer.WaitForAtLeastAsync(1, TestWaits.Liveness);

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

    /// <summary>
    /// Wire-shape proof for the memory-pressure stream (memory-watchdog plan, task 10):
    /// two AccountMemory records raised on the bus -- one a fresh, over-cap, target
    /// reading, one a stale unreadable one -- and asserts every proto field the mapping
    /// produces. Catches three distinct regressions a looser test would miss: (1) the
    /// growth_mb_per_hr unit conversion (source is bytes/hour, wire is MB/hour -- a
    /// forgotten /1024/1024 would fail the exact-value assert), (2) read_ok not carried
    /// through (a stale account silently reporting read_ok=true would slip past a test
    /// that only checked the fresh account), and (3) over_cap/is_target not staying false
    /// for the stale account (the proto comment promises this is always the case).
    /// </summary>
    [Fact]
    public async Task SubscribeMemoryPressure_FansOutEvents_ToSubscriber()
    {
        var registry = new InMemoryRegistry(Array.Empty<InstalledPlugin>());
        var bus = new InProcessPluginEventBus();
        var service = new PluginHostService(registry, "1.4.0", "1.0", HostStateOff(), NoAccounts(), bus, NoOpLauncher(), NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var writer = new TestStreamWriter<AccountMemorySnapshot>();
        using var cts = new CancellationTokenSource();
        var ctx = FakeServerCallContext.Create("/rororo.plugin.v1.RoRoRoHost/SubscribeMemoryPressure", cts.Token);
        var streamTask = service.SubscribeMemoryPressure(new SubscriptionRequest(), writer, ctx);

        // Give the subscription a tick to attach the event handler.
        await Task.Delay(20);

        var freshId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var staleId = Guid.Parse("00000000-0000-0000-0000-000000000004");

        // Fresh reading: 2 GiB private bytes, growing 320 MiB/hr, over the cap, and the
        // selected recycle target.
        bus.RaiseMemoryPressure(new AccountMemory(
            AccountId: freshId,
            PrivateBytes: 2L * 1024 * 1024 * 1024,
            GrowthBytesPerHour: 320.0 * 1024 * 1024,
            MinutesToCeiling: 45,
            OverCap: true,
            IsTarget: true,
            ReadOk: true));

        // Stale reading: process unreadable this tick. Last-known-good bytes carried
        // forward; the source (MemoryWatchdog.Sample) never lets over_cap/is_target be
        // true for an unreadable account, so both stay false here too.
        bus.RaiseMemoryPressure(new AccountMemory(
            AccountId: staleId,
            PrivateBytes: 1L * 1024 * 1024 * 1024,
            GrowthBytesPerHour: 0,
            MinutesToCeiling: 45, // aggregate projection, still valid machine-wide
            OverCap: false,
            IsTarget: false,
            ReadOk: false));

        await writer.WaitForAtLeastAsync(2, TestWaits.Liveness);

        cts.Cancel();
        await streamTask; // should complete cleanly on cancel

        Assert.Equal(2, writer.Written.Count);

        var fresh = writer.Written[0];
        Assert.Equal(freshId.ToString(), fresh.AccountId);
        Assert.Equal(2UL * 1024 * 1024 * 1024, fresh.PrivateBytes);
        Assert.Equal(320.0, fresh.GrowthMbPerHr, precision: 6);
        Assert.Equal(45u, fresh.MinsToCeiling);
        Assert.True(fresh.OverCap);
        Assert.True(fresh.IsTarget);
        Assert.True(fresh.ReadOk);

        var stale = writer.Written[1];
        Assert.Equal(staleId.ToString(), stale.AccountId);
        Assert.Equal(1UL * 1024 * 1024 * 1024, stale.PrivateBytes); // last-known-good, not zeroed
        Assert.Equal(0.0, stale.GrowthMbPerHr);
        Assert.False(stale.OverCap);
        Assert.False(stale.IsTarget);
        Assert.False(stale.ReadOk); // the flag a plugin must check before trusting PrivateBytes
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
    public async Task RequestLaunch_Refused_CarriesReasonCode()
    {
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()),
            "1.4.0", "1.0",
            HostStateOff(),
            NoAccounts(),
            new InProcessPluginEventBus(),
            new RefusingWithReasonCodeLaunchInvoker(),
            NoUITranslator(),
            NoActivity(), NoActivityMarker(), NoStopper());

        var result = await service.RequestLaunch(new LaunchRequest
        {
            AccountId = Guid.NewGuid().ToString(),
        }, FakeServerCallContext.Create());

        Assert.False(result.Ok);
        Assert.Equal("follow-target-not-joinable", result.ReasonCode);
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
    public async Task RequestLaunchTarget_PassesTheGameInstanceThrough_ToLauncher()
    {
        var fake = new FakeLaunchInvoker();
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), fake, NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        var acct = Guid.NewGuid().ToString();
        var job = Guid.NewGuid().ToString();
        var result = await service.RequestLaunchTarget(new LaunchTargetRequest
        {
            AccountId = acct,
            GameInstance = new GameInstance { PlaceId = 8737899170L, GameInstanceId = job },
        }, FakeServerCallContext.Create());

        Assert.True(result.Ok);
        var inv = Assert.Single(fake.TargetInvocations);
        Assert.Null(inv.url);
        Assert.Null(inv.follow);
        Assert.Equal(new PluginGameInstance(8737899170L, job), Assert.Single(fake.GameInstances));
    }

    [Fact]
    public async Task RequestLaunchTarget_AHalfGameInstance_StillReachesTheLauncherAsRequested()
    {
        var fake = new FakeLaunchInvoker();
        var service = new PluginHostService(
            new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), fake, NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper());

        await service.RequestLaunchTarget(new LaunchTargetRequest
        {
            AccountId = Guid.NewGuid().ToString(),
            GameInstance = new GameInstance { PlaceId = 5L },
        }, FakeServerCallContext.Create());

        // Not null: the adapter must see "game instance requested" and refuse it, never "no target".
        Assert.Equal(new PluginGameInstance(5L, ""), Assert.Single(fake.GameInstances));
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

    // =====================================================================
    // StopAccounts wait_for_exit (#244). The field report's hops each needed a
    // wait loop, a retry and a duplicate guard because stop returned on issue.
    // =====================================================================

    /// <summary>Thread-safe stopper whose clients leave the tracker a set time after the stop.</summary>
    private sealed class ExitingAccountStopper : IPluginAccountStopper
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _tracked;
        private readonly TimeSpan? _exitAfter;
        public ExitingAccountStopper(TimeSpan? exitAfter, params string[] tracked)
        {
            _exitAfter = exitAfter;
            _tracked = new HashSet<string>(tracked, StringComparer.Ordinal);
        }
        public IReadOnlyList<string> TrackedAccountIds { get { lock (_gate) return _tracked.ToList(); } }
        public bool StopAccount(string accountId)
        {
            lock (_gate) { if (!_tracked.Contains(accountId)) return false; }
            if (_exitAfter is { } after)
            {
                _ = Task.Delay(after).ContinueWith(_ => { lock (_gate) _tracked.Remove(accountId); });
            }
            return true;
        }
    }

    /// <summary>Running snapshot whose rows clear on request, standing in for IsRunning.</summary>
    private sealed class MutableRunningAccountsProvider : IRunningAccountsProvider
    {
        private readonly object _gate = new();
        private readonly List<RunningAccountSnapshot> _rows;
        public MutableRunningAccountsProvider(params string[] ids) =>
            _rows = ids.Select(id => new RunningAccountSnapshot(id, 1, "x", 100)).ToList();
        public void ClearAfter(string id, TimeSpan after) =>
            _ = Task.Delay(after).ContinueWith(_ => { lock (_gate) _rows.RemoveAll(r => r.AccountId == id); });
        public IReadOnlyList<RunningAccountSnapshot> Snapshot() { lock (_gate) return _rows.ToList(); }
    }

    private static PluginHostService WaitService(IPluginAccountStopper stopper, IRunningAccountsProvider running) =>
        new(new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), running, new InProcessPluginEventBus(), NoOpLauncher(),
            NoUITranslator(), NoActivity(), NoActivityMarker(), stopper)
        { StopWaitPoll = TimeSpan.FromMilliseconds(20) };

    [Fact]
    public async Task StopAccounts_WaitForExit_ReturnsOnlyOnceTheClientIsGone()
    {
        var a = Guid.NewGuid().ToString();
        var running = new MutableRunningAccountsProvider(a);
        running.ClearAfter(a, TimeSpan.FromMilliseconds(300));
        var service = WaitService(new ExitingAccountStopper(TimeSpan.FromMilliseconds(300), a), running);

        var request = new StopAccountsRequest { WaitForExit = true, TimeoutMs = 5000 };
        request.AccountIds.Add(a);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.StopAccounts(request, FakeServerCallContext.Create());

        Assert.Equal(a, Assert.Single(result.ExitedAccountIds));
        Assert.Empty(result.StillRunningAccountIds);
        Assert.True(sw.ElapsedMilliseconds >= 250, $"returned after {sw.ElapsedMilliseconds} ms, before the client left");
    }

    [Fact]
    public async Task StopAccounts_WaitForExit_DoesNotCallItGoneWhileTheRowStillReadsRunning()
    {
        // The real ordering: the tracker lets go first, and IsRunning (what the launch check reads)
        // clears a hop later on the exit event. A wait that checked only the tracker would return in
        // that gap, and the relaunch would meet "Account is already running" exactly as before.
        var a = Guid.NewGuid().ToString();
        var running = new MutableRunningAccountsProvider(a);
        running.ClearAfter(a, TimeSpan.FromMilliseconds(400));
        var service = WaitService(new ExitingAccountStopper(TimeSpan.Zero, a), running);

        var request = new StopAccountsRequest { WaitForExit = true, TimeoutMs = 5000 };
        request.AccountIds.Add(a);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.StopAccounts(request, FakeServerCallContext.Create());

        Assert.Equal(a, Assert.Single(result.ExitedAccountIds));
        Assert.True(sw.ElapsedMilliseconds >= 350, $"returned after {sw.ElapsedMilliseconds} ms, while the row still read running");
    }

    [Fact]
    public async Task StopAccounts_WaitForExit_ReportsStillRunningWhenTheWaitRunsOut()
    {
        var a = Guid.NewGuid().ToString();
        var service = WaitService(new ExitingAccountStopper(exitAfter: null, a), new MutableRunningAccountsProvider(a));

        var request = new StopAccountsRequest { WaitForExit = true, TimeoutMs = 200 };
        request.AccountIds.Add(a);
        var result = await service.StopAccounts(request, FakeServerCallContext.Create());

        Assert.Equal(1, result.StoppedCount);
        Assert.Empty(result.ExitedAccountIds);
        Assert.Equal(a, Assert.Single(result.StillRunningAccountIds));
    }

    [Fact]
    public async Task StopAccounts_WithoutWait_ReturnsOnIssueAndFillsNeitherList()
    {
        // Unchanged for every plugin built before 0.13.0.
        var a = Guid.NewGuid().ToString();
        var service = WaitService(new ExitingAccountStopper(exitAfter: null, a), new MutableRunningAccountsProvider(a));

        var request = new StopAccountsRequest();
        request.AccountIds.Add(a);
        var result = await service.StopAccounts(request, FakeServerCallContext.Create());

        Assert.Equal(1, result.StoppedCount);
        Assert.Empty(result.ExitedAccountIds);
        Assert.Empty(result.StillRunningAccountIds);
    }

    [Fact]
    public async Task StopAccounts_WaitForExit_UntrackedIdsStayInFailedOnly()
    {
        var tracked = Guid.NewGuid().ToString();
        var untracked = Guid.NewGuid().ToString();
        var service = WaitService(new ExitingAccountStopper(TimeSpan.Zero, tracked), new MutableRunningAccountsProvider());

        var request = new StopAccountsRequest { WaitForExit = true, TimeoutMs = 2000 };
        request.AccountIds.Add(untracked);
        request.AccountIds.Add(tracked);
        var result = await service.StopAccounts(request, FakeServerCallContext.Create());

        Assert.Equal(untracked, Assert.Single(result.FailedAccountIds));
        Assert.Equal(tracked, Assert.Single(result.ExitedAccountIds));
        Assert.DoesNotContain(untracked, result.StillRunningAccountIds);
    }

    // =====================================================================
    // Accounts loading (#243). "Known accounts: (none)" a minute before all three
    // came back cost the farm an hour offline.
    // =====================================================================

    private sealed class LoadingSavedAccountsProvider : ISavedAccountsProvider
    {
        private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly IReadOnlyList<SavedAccountSnapshot> _accounts;
        public LoadingSavedAccountsProvider(params SavedAccountSnapshot[] accounts) => _accounts = accounts;
        public void FinishLoading() => _loaded.TrySetResult();
        public bool IsLoaded => _loaded.Task.IsCompleted;
        public Task WhenLoadedAsync(CancellationToken ct) => _loaded.Task.WaitAsync(ct);
        // Empty until loaded, exactly as the real snapshot is.
        public IReadOnlyList<SavedAccountSnapshot> Snapshot() => IsLoaded ? _accounts : Array.Empty<SavedAccountSnapshot>();
    }

    private static readonly SavedAccountSnapshot Pokey =
        new("00000000-0000-0000-0000-000000000001", 12345, "Pokey", IsMain: true);

    private static PluginHostService LoadingService(ISavedAccountsProvider saved, IPluginLaunchInvoker? launcher = null) =>
        new(new InMemoryRegistry(Array.Empty<InstalledPlugin>()), "1.4.0", "1.0",
            HostStateOff(), NoAccounts(), new InProcessPluginEventBus(), launcher ?? NoOpLauncher(),
            NoUITranslator(), NoActivity(), NoActivityMarker(), NoStopper(), savedAccounts: saved)
        { AccountsLoadWait = TimeSpan.FromMilliseconds(200) };

    [Fact]
    public async Task GetAccounts_WhileAccountsLoad_WaitsAndAnswersWithTheRealList()
    {
        var saved = new LoadingSavedAccountsProvider(Pokey);
        var service = LoadingService(saved);
        service.AccountsLoadWait = TimeSpan.FromSeconds(5);

        var call = service.GetAccounts(new Empty(), FakeServerCallContext.Create());
        await Task.Delay(100);
        Assert.False(call.IsCompleted, "answered before the accounts were loaded");
        saved.FinishLoading();
        var list = await call;

        Assert.Equal("Pokey", Assert.Single(list.Accounts).DisplayName);
    }

    [Fact]
    public async Task GetAccounts_WhenLoadingOutlastsTheWait_RefusesRetryablyRatherThanAnsweringEmpty()
    {
        var service = LoadingService(new LoadingSavedAccountsProvider(Pokey));

        var ex = await Assert.ThrowsAsync<RpcException>(
            () => service.GetAccounts(new Empty(), FakeServerCallContext.Create()));

        Assert.Equal(StatusCode.Unavailable, ex.StatusCode);
        Assert.StartsWith("accounts-loading", ex.Status.Detail);
    }

    [Fact]
    public async Task RequestLaunch_WhileAccountsLoad_RefusesWithAReasonCodeAndNeverReachesTheLauncher()
    {
        var launcher = new FakeLaunchInvoker();
        var service = LoadingService(new LoadingSavedAccountsProvider(Pokey), launcher);

        var result = await service.RequestLaunch(new LaunchRequest { AccountId = Pokey.AccountId }, FakeServerCallContext.Create());

        Assert.False(result.Ok);
        Assert.Equal("accounts-loading", result.ReasonCode);
        Assert.Empty(launcher.Invocations);
    }

    [Fact]
    public async Task RequestLaunchTarget_WhileAccountsLoad_RefusesWithAReasonCodeAndNeverReachesTheLauncher()
    {
        var launcher = new FakeLaunchInvoker();
        var service = LoadingService(new LoadingSavedAccountsProvider(Pokey), launcher);

        var result = await service.RequestLaunchTarget(
            new LaunchTargetRequest { AccountId = Pokey.AccountId, FollowUserId = 1 }, FakeServerCallContext.Create());

        Assert.Equal("accounts-loading", result.ReasonCode);
        Assert.Empty(launcher.TargetInvocations);
    }

    [Fact]
    public async Task RequestLaunch_OnceLoaded_GoesThroughUnchanged()
    {
        var saved = new LoadingSavedAccountsProvider(Pokey);
        saved.FinishLoading();
        var launcher = new FakeLaunchInvoker();
        var service = LoadingService(saved, launcher);

        var result = await service.RequestLaunch(new LaunchRequest { AccountId = Pokey.AccountId }, FakeServerCallContext.Create());

        Assert.True(result.Ok);
        Assert.Equal(Pokey.AccountId, Assert.Single(launcher.Invocations));
    }

    [Fact]
    public async Task GetHostInfo_ReportsWhetherAccountsAreLoaded()
    {
        var saved = new LoadingSavedAccountsProvider(Pokey);
        var service = LoadingService(saved);

        Assert.False((await service.GetHostInfo(new Empty(), FakeServerCallContext.Create())).AccountsLoaded);
        saved.FinishLoading();
        Assert.True((await service.GetHostInfo(new Empty(), FakeServerCallContext.Create())).AccountsLoaded);
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
        public List<PluginGameInstance?> GameInstances { get; } = new();
        public CurrentServerInfo? Current { get; set; }

        public Task<(bool ok, string? failureReason, int processId, string? reasonCode)> RequestLaunchAsync(string accountId)
        {
            Invocations.Add(accountId);
            return Task.FromResult<(bool, string?, int, string?)>((true, null, 12345, null));
        }

        public Task<(bool ok, string? failureReason, int processId, string? reasonCode)> RequestLaunchTargetAsync(string accountId, string? shareUrl, long? followUserId, PluginGameInstance? gameInstance = null)
        {
            TargetInvocations.Add((accountId, shareUrl, followUserId));
            GameInstances.Add(gameInstance);
            return Task.FromResult<(bool, string?, int, string?)>((true, null, 6789, null));
        }

        public Task<CurrentServerInfo?> GetCurrentServerAsync() => Task.FromResult(Current);
    }

    /// <summary>Refuses every launch with a fixed reason code, so <c>RequestLaunch</c>'s
    /// <c>LaunchResult.ReasonCode</c> mapping can be tested without wiring a real flagged account.</summary>
    private sealed class RefusingWithReasonCodeLaunchInvoker : IPluginLaunchInvoker
    {
        public Task<(bool ok, string? failureReason, int processId, string? reasonCode)> RequestLaunchAsync(string accountId)
            => Task.FromResult<(bool, string?, int, string?)>((false, "x", 0, "follow-target-not-joinable"));

        public Task<(bool ok, string? failureReason, int processId, string? reasonCode)> RequestLaunchTargetAsync(string accountId, string? shareUrl, long? followUserId, PluginGameInstance? gameInstance = null)
            => Task.FromResult<(bool, string?, int, string?)>((false, "x", 0, "follow-target-not-joinable"));

        public Task<CurrentServerInfo?> GetCurrentServerAsync() => Task.FromResult<CurrentServerInfo?>(null);
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
