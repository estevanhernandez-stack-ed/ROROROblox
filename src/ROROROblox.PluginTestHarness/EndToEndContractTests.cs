using System.IO.Pipes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Plugins;
using ROROROblox.Core.Diagnostics;
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
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(),
            new StubActivityMarker(),
            new StubAccountStopper());

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
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(),
            new StubActivityMarker(),
            new StubAccountStopper());

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
    public async Task GetAccountActivity_ConsentedPlugin_ReturnsSnapshot()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";
        var accountId = Guid.NewGuid().ToString();

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
                Capabilities = new[] { "host.queries.account-activity" },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { "host.queries.account-activity" },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(
                new AccountActivitySnapshot(accountId, 1_700_000_000_000, 300)),
            new StubActivityMarker(),
            new StubAccountStopper());

        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: id => new[] { "host.queries.account-activity" });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            var resp = await client.GetAccountActivityAsync(new Empty());

            var item = Assert.Single(resp.Items);
            Assert.Equal(accountId, item.AccountId);
            Assert.Equal(300, item.SecondsSinceActivity);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetAccountActivity_DeniedWhenCapabilityNotGranted()
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
                // host.queries.account-activity is required by GetAccountActivity and is NOT granted.
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
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(),
            new StubActivityMarker(),
            new StubAccountStopper());

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
                await client.GetAccountActivityAsync(new Empty()));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    /// <summary>
    /// The honest end-to-end proof for the activity-crediting-fix plan: a REAL
    /// <see cref="ActivityMonitor"/> (fake probes + a mutable fake clock, same shape as
    /// ActivityMonitorTests) is wired through a REAL <see cref="AccountActivityMarker"/>
    /// into PluginHostService, and a REAL <see cref="ActivitySnapshotProvider"/> reads the
    /// SAME monitor instance. The account is launched and aged past the warn threshold,
    /// then the plugin calls MarkAccountActive over the real named-pipe gRPC pipe, then
    /// GetAccountActivity over the same pipe -- proving the full path RPC -> interceptor
    /// -> handler -> marker -> monitor -> snapshot without a single stub in the middle.
    /// </summary>
    [Fact]
    public async Task MarkAccountActive_GrantedPlugin_CreditsRealMonitor_VisibleInSubsequentSnapshot()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";
        var accountId = Guid.NewGuid();

        var clock = new FakeClock();
        var monitor = new ActivityMonitor(
            new NoOpForegroundProbe(), new NoOpInputClock(), new NoOpForegroundAccountResolver(), clock)
        {
            WarnThreshold = TimeSpan.FromMinutes(15),
        };
        monitor.OnAccountLaunched(accountId);
        clock.UtcNow = clock.UtcNow.AddMinutes(20); // age it well past the warn threshold

        var marker = new AccountActivityMarker(monitor, clock);
        var provider = new ActivitySnapshotProvider(monitor);

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
                Capabilities = new[]
                {
                    PluginCapability.HostCommandsMarkAccountActive,
                    PluginCapability.HostQueriesAccountActivity,
                },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[]
                {
                    PluginCapability.HostCommandsMarkAccountActive,
                    PluginCapability.HostQueriesAccountActivity,
                },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()),
            provider,
            marker,
            new StubAccountStopper());

        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: id => new[]
            {
                PluginCapability.HostCommandsMarkAccountActive,
                PluginCapability.HostQueriesAccountActivity,
            });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            await client.MarkAccountActiveAsync(new MarkAccountActiveRequest
            {
                AccountId = accountId.ToString(),
            });

            var resp = await client.GetAccountActivityAsync(new Empty());

            var item = Assert.Single(resp.Items);
            Assert.Equal(accountId.ToString(), item.AccountId);
            Assert.True(item.SecondsSinceActivity < 5,
                $"expected freshly-credited activity (<5s), got {item.SecondsSinceActivity}s");
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    [Fact]
    public async Task MarkAccountActive_DeniedWhenCapabilityNotGranted()
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
                // host.commands.mark-account-active is required by MarkAccountActive and is NOT granted.
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
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(),
            new StubActivityMarker(),
            new StubAccountStopper());

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
                await client.MarkAccountActiveAsync(new MarkAccountActiveRequest
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

    /// <summary>
    /// Mirrors <see cref="RequestLaunch_ProductionAccessor_FailsPreconditionWhenNoHeader"/> for
    /// MarkAccountActive: production wiring (accessor returns null), no x-plugin-id header on the
    /// call, gated method must reject with FailedPrecondition before ever reaching the marker.
    /// </summary>
    [Fact]
    public async Task MarkAccountActive_ProductionAccessor_FailsPreconditionWhenNoHeader()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";
        var marker = new StubActivityMarker();

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
                Capabilities = new[] { PluginCapability.HostCommandsMarkAccountActive },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { PluginCapability.HostCommandsMarkAccountActive },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(),
            marker,
            new StubAccountStopper());

        // Production shape: accessor returns null. Header is the only path to a plugin id.
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => null,
            consentLookup: id => new[] { PluginCapability.HostCommandsMarkAccountActive });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            // Deliberately NO x-plugin-id header.
            var ex = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.MarkAccountActiveAsync(new MarkAccountActiveRequest
                {
                    AccountId = Guid.NewGuid().ToString(),
                }));
            Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
            Assert.Empty(marker.MarkedAccountIds); // never reached the handler
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    /// <summary>
    /// Companion to <see cref="MarkAccountActive_ProductionAccessor_FailsPreconditionWhenNoHeader"/>:
    /// same production-shape wiring (accessor returns null), but the client sends the
    /// x-plugin-id header. The interceptor resolves the plugin id from the header and lets
    /// the call through to the marker.
    /// </summary>
    [Fact]
    public async Task MarkAccountActive_ProductionAccessor_ResolvesPluginIdFromHeader()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";
        var accountId = Guid.NewGuid().ToString();
        var marker = new StubActivityMarker();

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
                Capabilities = new[] { PluginCapability.HostCommandsMarkAccountActive },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { PluginCapability.HostCommandsMarkAccountActive },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(),
            marker,
            new StubAccountStopper());

        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => null,
            consentLookup: id => new[] { PluginCapability.HostCommandsMarkAccountActive });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            var headers = new Metadata { { "x-plugin-id", "626labs.test" } };
            await client.MarkAccountActiveAsync(
                new MarkAccountActiveRequest { AccountId = accountId },
                headers: headers);

            // The interceptor resolved the plugin id from the header and let the call
            // through to the marker -- if the header path were broken we'd get
            // RpcException(FailedPrecondition) before ever reaching it.
            var marked = Assert.Single(marker.MarkedAccountIds);
            Assert.Equal(accountId, marked);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    /// <summary>
    /// Proves <see cref="AccountActivityMarker"/>'s defensive parse end-to-end: an unparseable
    /// account id must not throw across the RPC boundary, and must leave the monitor untouched
    /// so a subsequent GetAccountActivity is unaffected.
    /// </summary>
    [Fact]
    public async Task MarkAccountActive_UnparseableAccountId_IsNoOpAndDoesNotThrow()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";

        var clock = new FakeClock();
        var monitor = new ActivityMonitor(
            new NoOpForegroundProbe(), new NoOpInputClock(), new NoOpForegroundAccountResolver(), clock);
        var marker = new AccountActivityMarker(monitor, clock);
        var provider = new ActivitySnapshotProvider(monitor);

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
                Capabilities = new[]
                {
                    PluginCapability.HostCommandsMarkAccountActive,
                    PluginCapability.HostQueriesAccountActivity,
                },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[]
                {
                    PluginCapability.HostCommandsMarkAccountActive,
                    PluginCapability.HostQueriesAccountActivity,
                },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()),
            provider,
            marker,
            new StubAccountStopper());

        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: id => new[]
            {
                PluginCapability.HostCommandsMarkAccountActive,
                PluginCapability.HostQueriesAccountActivity,
            });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            // Must not throw despite the unparseable id.
            await client.MarkAccountActiveAsync(new MarkAccountActiveRequest
            {
                AccountId = "not-a-guid",
            });

            var resp = await client.GetAccountActivityAsync(new Empty());
            Assert.Empty(resp.Items); // nothing was ever launched/tracked; the bad id touched nothing
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
            new PluginUITranslator(uiHost),
            new StubActivityProvider(),
            new StubActivityMarker(),
            new StubAccountStopper());

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
    /// StopAccounts over the real pipe: denied without consent, honored with it, and an
    /// untracked account id reports as failed rather than throwing. The recovery half of
    /// the agent-ops surface — an agent clears dead clients before relaunching them.
    /// </summary>
    [Fact]
    public async Task StopAccounts_DeniedWhenCapabilityNotGranted()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";
        var stopper = new StubAccountStopper { Tracked = { Guid.NewGuid().ToString() } };

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
                // host.commands.stop-accounts is required by StopAccounts and is NOT granted.
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
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(),
            new StubActivityMarker(),
            stopper);

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
                await client.StopAccountsAsync(new StopAccountsRequest()));
            Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
            Assert.Empty(stopper.Stopped); // never reached the handler
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    [Fact]
    public async Task StopAccounts_GrantedPlugin_StopsTrackedAndReportsUntracked()
    {
        var pipeName = $"rororo-plugin-test-{Guid.NewGuid():N}";
        var tracked = Guid.NewGuid().ToString();
        var untracked = Guid.NewGuid().ToString();
        var stopper = new StubAccountStopper { Tracked = { tracked } };

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
                Capabilities = new[] { PluginCapability.HostCommandsStopAccounts },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { PluginCapability.HostCommandsStopAccounts },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(),
            new StubActivityMarker(),
            stopper);

        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => "626labs.test",
            consentLookup: id => new[] { PluginCapability.HostCommandsStopAccounts });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            var request = new StopAccountsRequest();
            request.AccountIds.Add(untracked);
            request.AccountIds.Add(tracked);
            var result = await client.StopAccountsAsync(request);

            Assert.Equal(1, result.StoppedCount);
            Assert.Equal(untracked, Assert.Single(result.FailedAccountIds));
            Assert.Equal(tracked, Assert.Single(stopper.Stopped));
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    /// <summary>
    /// The capability map must cover every RoRoRoHost method, or the host refuses to start.
    /// This is the guard that would have caught the UpdateUI/RemoveUI fail-open before ship:
    /// PluginHostStartupService.StartAsync calls AssertExhaustive() before binding the pipe.
    /// </summary>
    [Fact]
    public void CapabilityMap_CoversEveryHostMethod()
        => RpcMethodCapabilityMap.AssertExhaustive();

    /// <summary>
    /// Regression guard for the vibe-access verify finding (run f7ebdbd1): UpdateUI and
    /// RemoveUI accepted a bogus UIHandle from a caller that never created any UI. Handle
    /// ownership is the ONLY gate on these two RPCs (the capability map deliberately leaves
    /// them ungated), so over the real pipe: a bogus handle must refuse with
    /// PermissionDenied, an owned handle must work, and a removed handle must refuse on
    /// second use.
    /// </summary>
    [Fact]
    public async Task UpdateUI_RemoveUI_RefuseForeignHandle_HonorOwnedHandle()
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
                Capabilities = new[] { PluginCapability.HostUITrayMenu },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { PluginCapability.HostUITrayMenu },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(uiHost),
            new StubActivityProvider(),
            new StubActivityMarker(),
            new StubAccountStopper());

        // Production shape: accessor returns null; the x-plugin-id header is the identity.
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => null,
            consentLookup: id => new[] { PluginCapability.HostUITrayMenu });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);
            var headers = new Metadata { { "x-plugin-id", "626labs.test" } };

            // Bogus handle from a caller that owns nothing — the verify driver's exact call.
            var updateEx = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.UpdateUIAsync(new UIUpdate
                {
                    Handle = new UIHandle { Id = "va-bogus-handle" },
                    MenuItem = new MenuItemSpec { Label = "x" },
                }, headers: headers));
            Assert.Equal(StatusCode.PermissionDenied, updateEx.StatusCode);

            var removeEx = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.RemoveUIAsync(new UIHandle { Id = "va-bogus-handle" }, headers: headers));
            Assert.Equal(StatusCode.PermissionDenied, removeEx.StatusCode);
            Assert.Empty(uiHost.RemovedHandles);

            // Owned handle: create, update, remove — all honored.
            var handle = await client.AddTrayMenuItemAsync(
                new MenuItemSpec { Label = "mine", Tooltip = "t", Enabled = true }, headers: headers);

            await client.UpdateUIAsync(new UIUpdate
            {
                Handle = handle,
                MenuItem = new MenuItemSpec { Label = "renamed" },
            }, headers: headers);

            await client.RemoveUIAsync(handle, headers: headers);
            Assert.Equal(handle.Id, Assert.Single(uiHost.RemovedHandles));

            // Removed handle no longer exists — second remove refuses.
            var goneEx = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.RemoveUIAsync(handle, headers: headers));
            Assert.Equal(StatusCode.PermissionDenied, goneEx.StatusCode);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    /// <summary>
    /// Regression guard for bug #3 (fixed at 652c43a). Production wiring at
    /// App.xaml.cs:426 passes <c>currentPluginAccessor: () =&gt; null</c> and depends
    /// entirely on the <c>x-plugin-id</c> request header to resolve the calling plugin.
    /// The other tests in this file pass a fixed accessor, which silently masks any
    /// bug in the header-read path — that's exactly why bug #3 shipped to M3
    /// unnoticed. This test mirrors production: accessor returns null, no header on
    /// the call, gated method (RequestLaunch) must reject with FailedPrecondition.
    /// </summary>
    [Fact]
    public async Task RequestLaunch_ProductionAccessor_FailsPreconditionWhenNoHeader()
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
                Capabilities = new[] { "host.commands.request-launch" },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { "host.commands.request-launch" },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(),
            new StubActivityMarker(),
            new StubAccountStopper());

        // Production shape: accessor returns null. Header is the only path to a plugin id.
        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => null,
            consentLookup: id => new[] { "host.commands.request-launch" });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            // Deliberately NO x-plugin-id header. Production accessor returns null,
            // so the interceptor has no way to resolve the calling plugin's id.
            var ex = await Assert.ThrowsAsync<RpcException>(async () =>
                await client.RequestLaunchAsync(new LaunchRequest
                {
                    AccountId = Guid.NewGuid().ToString(),
                }));
            Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
        }
        finally
        {
            await startup.StopAsync(CancellationToken.None);
            await startup.DisposeAsync();
        }
    }

    /// <summary>
    /// Companion to <see cref="RequestLaunch_ProductionAccessor_FailsPreconditionWhenNoHeader"/>:
    /// same production-shape wiring (accessor returns null), but the client sends the
    /// <c>x-plugin-id</c> header. The interceptor reads the header, resolves to the
    /// granted capability set, and lets the call through to the launcher stub.
    /// </summary>
    [Fact]
    public async Task RequestLaunch_ProductionAccessor_ResolvesPluginIdFromHeader()
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
                Capabilities = new[] { "host.commands.request-launch" },
            },
            InstallDir = Path.GetTempPath(),
            Consent = new ConsentRecord
            {
                PluginId = "626labs.test",
                GrantedCapabilities = new[] { "host.commands.request-launch" },
                AutostartEnabled = false,
            },
        });

        var hostService = new PluginHostService(
            registry, "1.4.0", "1.0",
            new FixedHostState("On"),
            new EmptyAccounts(),
            new InProcessPluginEventBus(),
            new NoOpLauncher(),
            new PluginUITranslator(new NullUIHost()),
            new StubActivityProvider(),
            new StubActivityMarker(),
            new StubAccountStopper());

        var interceptor = new CapabilityInterceptor(
            currentPluginAccessor: () => null,
            consentLookup: id => new[] { "host.commands.request-launch" });

        var startup = new PluginHostStartupService(
            hostService, interceptor,
            NullLogger<PluginHostStartupService>.Instance,
            pipeName);

        await startup.StartAsync(CancellationToken.None);
        try
        {
            using var channel = ConnectChannel(pipeName);
            var client = new RoRoRoHost.RoRoRoHostClient(channel);

            var headers = new Metadata { { "x-plugin-id", "626labs.test" } };
            var result = await client.RequestLaunchAsync(
                new LaunchRequest { AccountId = Guid.NewGuid().ToString() },
                headers: headers);

            // NoOpLauncher returns (false, "test stub", 0). The point isn't that the
            // launcher succeeds — it's that the interceptor RESOLVED the plugin id
            // from the header and let the call through. If the header path were broken,
            // we'd get RpcException(FailedPrecondition) before ever reaching the launcher.
            Assert.False(result.Ok);
            Assert.Equal("test stub", result.FailureReason);
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

    // ---- ActivityMonitor fakes for the real-monitor round-trip tests. Same shape as
    // ROROROblox.Tests.ActivityMonitorTests's private fakes (they can't be shared across
    // assemblies), pared down to what MarkAccountActive's path actually exercises: a
    // mutable clock, plus no-op foreground/input/resolver since Sample() is never called here.

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class NoOpForegroundProbe : IForegroundWindowProbe
    {
        public bool TryGetForegroundPid(out int pid) { pid = 0; return false; }
    }

    private sealed class NoOpInputClock : ISystemInputClock
    {
        public uint LastInputTick => 0;
    }

    private sealed class NoOpForegroundAccountResolver : IForegroundAccountResolver
    {
        public bool TryResolveAccountByPid(int pid, out Guid accountId)
        {
            accountId = Guid.Empty;
            return false;
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

        public Task<(bool ok, string? failureReason, int processId)> RequestLaunchTargetAsync(
            string accountId, string? shareUrl, long? followUserId)
            => Task.FromResult<(bool, string?, int)>((false, "test stub", 0));

        public Task<CurrentServerInfo?> GetCurrentServerAsync()
            => Task.FromResult<CurrentServerInfo?>(null);
    }

    private sealed class StubAccountStopper : IPluginAccountStopper
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
