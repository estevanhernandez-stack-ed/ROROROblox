using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.App.Discord.Internal;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Driven entirely through the <see cref="IDiscordRpcClient"/> test seam — no real Discord IPC.
/// Each test wires a <see cref="FakeDiscordRpcClient"/> via the internal test-only constructor.
/// </summary>
public class DiscordRichPresenceServiceTests
{
    private const string TestAppId = "1501748116985221272";

    [Fact]
    public async Task StartAsync_DoesNotInitialize_WhenAppIdMissing()
    {
        var config = new FakeConfig(richPresenceEnabled: true);
        var fake = new FakeDiscordRpcClient();
        var service = NewService(config, fake, appId: "");

        await service.StartAsync(CancellationToken.None);

        Assert.False(fake.InitializeCalled);
    }

    [Fact]
    public async Task StartAsync_DoesNotInitialize_WhenRichPresenceDisabled()
    {
        var config = new FakeConfig(richPresenceEnabled: false);
        var fake = new FakeDiscordRpcClient();
        var service = NewService(config, fake);

        await service.StartAsync(CancellationToken.None);

        Assert.False(fake.InitializeCalled);
    }

    [Fact]
    public async Task StartAsync_InitializesClient_WhenEnabledAndAppIdSet()
    {
        var config = new FakeConfig(richPresenceEnabled: true);
        var fake = new FakeDiscordRpcClient();
        var service = NewService(config, fake);

        await service.StartAsync(CancellationToken.None);

        Assert.True(fake.InitializeCalled);
    }

    [Fact]
    public async Task UpdateStateAsync_AccountsActive_SetsActiveAssetKeys()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);

        await service.UpdateStateAsync(new RichPresenceState(PresenceMode.AccountsActive, 2, "Multi-clienting"), CancellationToken.None);

        Assert.NotNull(fake.LastPresence);
        Assert.Equal(DiscordRichPresenceService.ActiveLargeKey, fake.LastPresence!.LargeImageKey);
        Assert.Equal(DiscordRichPresenceService.ActiveSmallKey, fake.LastPresence.SmallImageKey);
        Assert.Equal("2 accounts active", fake.LastPresence.Details);
        Assert.Equal("Multi-clienting", fake.LastPresence.State);
    }

    [Fact]
    public async Task UpdateStateAsync_Idle_SetsIdleAssetKeys()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);

        await service.UpdateStateAsync(new RichPresenceState(PresenceMode.Idle, 0, null), CancellationToken.None);

        Assert.NotNull(fake.LastPresence);
        Assert.Equal(DiscordRichPresenceService.IdleLargeKey, fake.LastPresence!.LargeImageKey);
        Assert.Equal(DiscordRichPresenceService.IdleSmallKey, fake.LastPresence.SmallImageKey);
        Assert.Equal("Idle", fake.LastPresence.Details);
    }

    [Fact]
    public async Task UpdateStateAsync_AccountsActive_SingularGrammar()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);

        await service.UpdateStateAsync(new RichPresenceState(PresenceMode.AccountsActive, 1, null), CancellationToken.None);

        Assert.Equal("1 account active", fake.LastPresence!.Details);
    }

    [Fact]
    public async Task UpdateStateAsync_NoOp_WhenServiceNotStarted()
    {
        var config = new FakeConfig(richPresenceEnabled: true);
        var fake = new FakeDiscordRpcClient();
        var service = NewService(config, fake);

        // Did not call StartAsync — _client is null
        await service.UpdateStateAsync(new RichPresenceState(PresenceMode.AccountsActive, 1, null), CancellationToken.None);

        Assert.Null(fake.LastPresence);
        Assert.False(fake.InitializeCalled);
    }

    [Fact]
    public async Task SetPartyAsync_BuildsHashedPartyIdAndBase64JoinSecret()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);
        const string shareUrl = "https://www.roblox.com/games/123/x?privateServerLinkCode=ABCDEF";

        await service.SetPartyAsync(shareUrl, CancellationToken.None);

        Assert.NotNull(fake.LastPresence?.Party);
        Assert.Equal(DiscordRichPresenceService.HashPartyId(shareUrl), fake.LastPresence!.Party!.PartyId);
        Assert.Equal(16, fake.LastPresence.Party.PartyId.Length);
        Assert.Equal(DiscordRichPresenceService.PartyMaxSize, fake.LastPresence.Party.MaxSize);
        Assert.Equal(shareUrl, DiscordRichPresenceService.DecodeJoinSecret(fake.LastPresence.Party.JoinSecret));
    }

    [Fact]
    public async Task SetPartyAsync_NoOp_OnEmptyShareUrl()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);

        await service.SetPartyAsync("", CancellationToken.None);

        // No new presence emitted past whatever StartAsync did (which is none, since we don't
        // call UpdateStateAsync from StartAsync — Lifecycle is responsible).
        Assert.Null(fake.LastPresence);
    }

    [Fact]
    public async Task ClearPartyAsync_EmitsPresenceWithoutParty()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);
        await service.SetPartyAsync("https://x/?accessCode=A", CancellationToken.None);
        Assert.NotNull(fake.LastPresence?.Party);

        await service.ClearPartyAsync(CancellationToken.None);

        Assert.NotNull(fake.LastPresence);
        Assert.Null(fake.LastPresence!.Party);
    }

    [Fact]
    public async Task JoinRequested_DecodesBase64Secret_IntoEventArgs()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);
        const string shareUrl = "https://www.roblox.com/games/9/y?accessCode=Z";
        var encoded = DiscordRichPresenceService.EncodeJoinSecret(shareUrl);

        string? captured = null;
        service.JoinRequested += (_, args) => captured = args.ServerShareUrl;

        fake.RaiseJoinRequested(encoded);

        Assert.Equal(shareUrl, captured);
    }

    [Fact]
    public async Task JoinRequested_BadBase64_DoesNotThrow_AndDoesNotRaise()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);
        var raised = false;
        service.JoinRequested += (_, _) => raised = true;

        fake.RaiseJoinRequested("not_valid_base64!!!");

        Assert.False(raised);
    }

    [Fact]
    public void ReconnectBackoffLadder_Matches_5_10_20_40_60()
    {
        Assert.Equal(new[] { 5, 10, 20, 40, 60 }, DiscordRichPresenceService.ReconnectBackoffSeconds);
    }

    [Fact]
    public async Task ConfigChanged_FlipsOff_TearsDownClient()
    {
        var config = new FakeConfig(richPresenceEnabled: true);
        var fake = new FakeDiscordRpcClient();
        var service = NewService(config, fake);
        await service.StartAsync(CancellationToken.None);
        Assert.True(fake.InitializeCalled);

        config.SetEnabled(false);

        Assert.True(fake.DeinitializeCalled);
        Assert.True(fake.DisposeCalled);
    }

    [Fact]
    public async Task ConfigChanged_FlipsOn_AfterStartedDisabled_ConnectsClient()
    {
        var config = new FakeConfig(richPresenceEnabled: false);
        var fakes = new Queue<FakeDiscordRpcClient>();
        fakes.Enqueue(new FakeDiscordRpcClient());
        var service = NewServiceWithFactory(config, _ =>
        {
            if (fakes.Count == 0) fakes.Enqueue(new FakeDiscordRpcClient());
            return fakes.Peek();
        });

        await service.StartAsync(CancellationToken.None);
        // Disabled at start → no init.
        Assert.False(fakes.Peek().InitializeCalled);

        config.SetEnabled(true);

        Assert.True(fakes.Peek().InitializeCalled);
    }

    [Fact]
    public async Task DisposeAsync_TearsDown_AndIsIdempotent()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);

        await service.DisposeAsync();
        await service.DisposeAsync(); // second call must not throw

        Assert.True(fake.DisposeCalled);
    }

    [Fact]
    public void HashPartyId_Deterministic_For_SameUrl()
    {
        const string url = "https://www.roblox.com/games/1?accessCode=A";
        var a = DiscordRichPresenceService.HashPartyId(url);
        var b = DiscordRichPresenceService.HashPartyId(url);
        Assert.Equal(a, b);
        Assert.Equal(16, a.Length);
    }

    [Fact]
    public void EncodeJoinSecret_RoundTrips()
    {
        const string url = "https://www.roblox.com/games/1?privateServerLinkCode=XYZ&placeId=42";
        var encoded = DiscordRichPresenceService.EncodeJoinSecret(url);
        var decoded = DiscordRichPresenceService.DecodeJoinSecret(encoded);
        Assert.Equal(url, decoded);
    }

    // ---- Helpers ----

    private static DiscordRichPresenceService NewService(IDiscordConfig config, IDiscordRpcClient client, string appId = TestAppId)
    {
        var appConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Discord:ApplicationId"] = appId,
            })
            .Build();
        return new DiscordRichPresenceService(
            config,
            NullLogger<DiscordRichPresenceService>.Instance,
            appConfig,
            defaultClientFactory: _ => client);
    }

    private static DiscordRichPresenceService NewServiceWithFactory(IDiscordConfig config, Func<string, IDiscordRpcClient> factory)
    {
        var appConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Discord:ApplicationId"] = TestAppId,
            })
            .Build();
        return new DiscordRichPresenceService(
            config,
            NullLogger<DiscordRichPresenceService>.Instance,
            appConfig,
            defaultClientFactory: factory);
    }

    private static (DiscordRichPresenceService service, FakeDiscordRpcClient fake) StartedService(bool richPresenceEnabled)
    {
        var config = new FakeConfig(richPresenceEnabled);
        var fake = new FakeDiscordRpcClient();
        var service = NewService(config, fake);
        service.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (service, fake);
    }

    /// <summary>Mutable IDiscordConfig fake; raises Changed on flips to drive the service's reactive paths.</summary>
    private sealed class FakeConfig : IDiscordConfig
    {
        private bool _enabled;

        public FakeConfig(bool richPresenceEnabled)
        {
            _enabled = richPresenceEnabled;
            WebhookEvents = DiscordWebhookEvents.AllOff;
        }

        public bool RichPresenceEnabled => _enabled;
        public string? WebhookUrl { get; set; }
        public DiscordWebhookEvents WebhookEvents { get; set; }

        public void SetEnabled(bool value)
        {
            _enabled = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public Task SaveAsync(DiscordConfigSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;
        public event EventHandler? Changed;
    }

    /// <summary>In-memory fake — records every interaction; surfaces RaiseJoinRequested/RaiseConnectionFailed/RaiseReady so tests can drive the service.</summary>
    internal sealed class FakeDiscordRpcClient : IDiscordRpcClient
    {
        public bool InitializeCalled { get; private set; }
        public bool DeinitializeCalled { get; private set; }
        public bool DisposeCalled { get; private set; }
        public bool ClearPresenceCalled { get; private set; }
        public DiscordPresencePayload? LastPresence { get; private set; }
        public bool IsInitialized => InitializeCalled && !DeinitializeCalled && !DisposeCalled;

        public void Initialize() => InitializeCalled = true;
        public void Deinitialize() => DeinitializeCalled = true;
        public void ClearPresence() => ClearPresenceCalled = true;
        public void SetPresence(DiscordPresencePayload payload) => LastPresence = payload;

        public event EventHandler<string>? JoinRequested;
        public event EventHandler? ConnectionFailed;
        public event EventHandler? Ready;

        public void RaiseJoinRequested(string secret) => JoinRequested?.Invoke(this, secret);
        public void RaiseConnectionFailed() => ConnectionFailed?.Invoke(this, EventArgs.Empty);
        public void RaiseReady() => Ready?.Invoke(this, EventArgs.Empty);

        public void Dispose() => DisposeCalled = true;
    }
}
