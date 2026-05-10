using System.IO.Pipes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;

namespace ROROROblox.PluginTestHarness;

/// <summary>
/// End-to-end contract tests: real Kestrel-hosted gRPC server bound to a
/// per-test named pipe, real Grpc.Net.Client channel that dials the pipe,
/// and the full proto / serializer / interceptor pipeline. The integration
/// these tests nail down is the M2 + M3 gate — handshake, capability gating,
/// UI translator round-trip, and (deferred) consent revocation.
/// </summary>
public class EndToEndContractTests
{
    [Fact]
    public async Task PluginConnectsAndHandshakeSucceeds()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";

        var registry = new SingleInstalledPluginLookup(new InstalledPlugin
        {
            Manifest = new PluginManifest
            {
                SchemaVersion = 1,
                Id = "626labs.test",
                Name = "Test",
                Version = "1.0",
                ContractVersion = "1.0",
                Publisher = "626",
                Description = "x",
                Capabilities = new[] { "host.events.account-launched" },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { "host.events.account-launched" },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()));

        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: id => new[] { "host.events.account-launched" });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);

        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            var response = await client.HandshakeAsync(new HandshakeRequest
            {
                PluginId = "626labs.test",
                ContractVersion = "1.0",
            });

            Assert.True(response.Accepted);
            Assert.Equal("1.4.0", response.HostVersion);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    [Fact]
    public async Task RequestLaunch_DeniedWhenCapabilityNotGranted()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";

        var registry = new SingleInstalledPluginLookup(new InstalledPlugin
        {
            Manifest = new PluginManifest
            {
                SchemaVersion = 1,
                Id = "626labs.test",
                Name = "Test",
                Version = "1.0",
                ContractVersion = "1.0",
                Publisher = "626",
                Description = "x",
                // Plugin only declares (and consents to) the events capability.
                // host.commands.request-launch is required by RequestLaunch and is NOT granted.
                Capabilities = new[] { "host.events.account-launched" },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { "host.events.account-launched" },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()));

        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: id => new[] { "host.events.account-launched" });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            var ex = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.RequestLaunchAsync(new LaunchRequest
                {
                    AccountId = Guid.NewGuid().ToString(),
                }));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    [Fact]
    public async Task AddTrayMenuItem_RoundTrips_AndRecordsOnHost()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";
        var uiHost = new RecordingUIHost();

        var registry = new SingleInstalledPluginLookup(new InstalledPlugin
        {
            Manifest = new PluginManifest
            {
                SchemaVersion = 1,
                Id = "626labs.test",
                Name = "Test",
                Version = "1.0",
                ContractVersion = "1.0",
                Publisher = "626",
                Description = "x",
                Capabilities = new[] { "host.ui.tray-menu" },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { "host.ui.tray-menu" },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(uiHost));

        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: id => new[] { "host.ui.tray-menu" });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            var handle = await client.AddTrayMenuItemAsync(new MenuItemSpec
            {
                Label = "Toggle auto-keys",
                Tooltip = "Test",
                Enabled = true,
            });

            Assert.NotEmpty(handle.Id);
            var recorded = Assert.Single(uiHost.AddedTrayItems);
            Assert.Equal("Toggle auto-keys", recorded.label);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    /// <summary>
    /// DEFERRED to v1.5+. v1.4 ships call-entry capability gating only
    /// (CapabilityInterceptor only inspects the metadata at RPC start, not mid-stream).
    /// The spec calls for a 5s grace period before forcibly killing an open stream
    /// when consent is revoked — that needs:
    ///   1. PluginHostService exposes OnConsentRevoked(pluginId) — cancels any open
    ///      streams for that plugin via a CancellationTokenSource registry.
    ///   2. ConsentStore.RevokeAsync raises an event that App.xaml.cs wires to the
    ///      PluginHostService callback above.
    ///   3. Open streams observe the cancellation within ~1s in CI (5s in prod per spec).
    /// Mid-stream revocation is a meaningful surface — race-safe stream cancellation,
    /// consent-change push events, server-side stream registry. Worth doing right
    /// rather than rushed at the M3 line. See plan task 24 step 2 for the v1.5 work.
    /// </summary>
    [Fact(Skip = "Mid-stream consent revocation deferred to v1.5+. v1.4 ships call-entry capability gating only. See plan task 24 step 2 for the v1.5 work.")]
    public async Task ConsentRevocation_CancelsActiveStream_WithinOneSecond()
    {
        await Task.CompletedTask;
    }

    private static GrpcChannel ConnectChannel(string pipeName)
    {
        return GrpcChannel.ForAddress("http://pipe", new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                ConnectCallback = async (ctx, ct) =>
                {
                    var pipe = new NamedPipeClientStream(".", pipeName,
                        PipeDirection.InOut, PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(ct);
                    return pipe;
                },
            },
        });
    }

    private sealed class SingleInstalledPluginLookup : IInstalledPluginsLookup
    {
        private readonly InstalledPlugin _plugin;
        public SingleInstalledPluginLookup(InstalledPlugin p) { _plugin = p; }
        public InstalledPlugin? FindById(string id) => id == _plugin.Manifest.Id ? _plugin : null;
    }

    private sealed class FixedHostState : IPluginHostStateProvider
    {
        public FixedHostState(string s) { MultiInstanceState = s; }
        public bool MultiInstanceEnabled => MultiInstanceState == "On";
        public string MultiInstanceState { get; }
    }

    private sealed class EmptyAccounts : IRunningAccountsProvider
    {
        public IReadOnlyList<RunningAccountSnapshot> Snapshot() => Array.Empty<RunningAccountSnapshot>();
    }

    private sealed class NoOpLauncher : IPluginLaunchInvoker
    {
        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchAsync(string accountId)
            => Task.FromResult<(bool, string?, int)>((false, "test stub", 0));
    }

    private sealed class NullUIHost : IPluginUIHost
    {
        public string AddTrayMenuItem(string p, string l, string? t, bool e, Action c) => string.Empty;
        public string AddRowBadge(string p, string t, string? c, string? tt) => string.Empty;
        public string AddStatusPanel(string p, string t, string b) => string.Empty;
        public void Update(string h, string l) { }
        public void Remove(string h) { }
    }

    private sealed class RecordingUIHost : IPluginUIHost
    {
        public List<(string pluginId, string label)> AddedTrayItems { get; } = new();
        public List<string> RemovedHandles { get; } = new();
        private int _next = 1;
        public string AddTrayMenuItem(string p, string l, string? t, bool e, Action c)
        {
            AddedTrayItems.Add((p, l));
            return $"handle-{_next++}";
        }
        public string AddRowBadge(string p, string t, string? c, string? tt) => $"handle-{_next++}";
        public string AddStatusPanel(string p, string t, string b) => $"handle-{_next++}";
        public void Update(string h, string l) { }
        public void Remove(string h) => RemovedHandles.Add(h);
    }
}
