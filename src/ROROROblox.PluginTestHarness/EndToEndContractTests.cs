using System.IO.Pipes;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins;
using ROROROblox.PluginContract;

namespace ROROROblox.PluginTestHarness;

/// <summary>
/// End-to-end contract test: a real Kestrel-hosted gRPC server bound to a
/// per-test named pipe, a real Grpc.Net.Client channel that dials the pipe,
/// and a Handshake RPC that exercises the full proto / serializer / interceptor
/// pipeline. The integration this nails down is the M2 gate — if the named-pipe
/// transport handshake works, the rest of the surface (events, commands, UI)
/// follows the same wire path.
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
            using var channel = GrpcChannel.ForAddress("http://pipe", new GrpcChannelOptions
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
}
