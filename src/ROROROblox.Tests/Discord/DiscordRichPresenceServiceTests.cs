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
    public async Task StartAsync_PushesInitialIdlePresence_SoDiscordHasSomethingToShow()
    {
        // Regression guard for CHECKPOINT 1.5 fix: without this, the IPC pipe is open but
        // Discord shows nothing on the user's profile until OnAccountStarted fires from
        // DiscordPresenceLifecycle. We push Idle right after Initialize so the user
        // immediately sees "Playing ROROROblox · Idle" the moment the app opens.
        var config = new FakeConfig(richPresenceEnabled: true);
        var fake = new FakeDiscordRpcClient();
        var service = NewService(config, fake);

        await service.StartAsync(CancellationToken.None);

        Assert.NotNull(fake.LastPresence);
        Assert.Equal(DiscordRichPresenceService.IdleLargeKey, fake.LastPresence!.LargeImageKey);
        Assert.Equal("Idle", fake.LastPresence.Details);
        Assert.Null(fake.LastPresence.Party);
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
    public async Task SetPartyAsync_BuildsHashedPartyIdAndCompactJoinSecret()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);
        // Realistic launcher-shape URL — what RobloxLauncher.BuildPlaceLauncherUrl emits.
        const string shareUrl = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId=920587237&linkCode=ABCDEF1234567890";

        await service.SetPartyAsync(shareUrl, CancellationToken.None);

        Assert.NotNull(fake.LastPresence?.Party);
        Assert.Equal(DiscordRichPresenceService.HashPartyId(shareUrl), fake.LastPresence!.Party!.PartyId);
        Assert.Equal(16, fake.LastPresence.Party.PartyId.Length);
        Assert.Equal(DiscordRichPresenceService.PartyMaxSize, fake.LastPresence.Party.MaxSize);

        // CHECKPOINT 1.8 fix: JoinSecret must be ≤128 chars (Lachee's Secrets.JoinSecret cap).
        // Compact format starts with "P|" and embeds placeId + linkCode + accessCode.
        Assert.True(fake.LastPresence.Party.JoinSecret.Length <= 128,
            $"JoinSecret length {fake.LastPresence.Party.JoinSecret.Length} exceeds Discord's 128-char cap.");
        Assert.StartsWith("P|", fake.LastPresence.Party.JoinSecret);

        // Decoder reconstructs a launchable PlaceLauncher.ashx URL with the same placeId + linkCode.
        var decoded = DiscordRichPresenceService.DecodeJoinSecret(fake.LastPresence.Party.JoinSecret);
        Assert.Contains("placeId=920587237", decoded);
        Assert.Contains("linkCode=ABCDEF1234567890", decoded);
        Assert.Contains("PlaceLauncher.ashx", decoded);
        Assert.Contains("request=RequestPrivateGame", decoded);
    }

    [Fact]
    public async Task SetPartyAsync_NoOp_OnEmptyShareUrl()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);

        // StartAsync pushed the initial Idle presence (CHECKPOINT 1.5 fix). Snapshot it,
        // then assert SetPartyAsync(empty) does NOT overwrite it.
        var beforeParty = fake.LastPresence;
        Assert.NotNull(beforeParty);
        Assert.Null(beforeParty!.Party);

        await service.SetPartyAsync("", CancellationToken.None);

        // No party emitted; presence still the initial Idle (no overwrite from empty-url path).
        Assert.Same(beforeParty, fake.LastPresence);
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
    public async Task UpdateStateAsync_PreservesParty_AfterSetPartyAsync()
    {
        // Regression guard for CHECKPOINT 1.7 fix: lifecycle's UpdateStateAsync was wiping the
        // party milliseconds after SetPartyAsync set it (because it built the payload with
        // party=null). The Join button would appear briefly then vanish as soon as the
        // RobloxPlayerBeta attached and AccountStarted fired.
        var (service, fake) = StartedService(richPresenceEnabled: true);
        await service.SetPartyAsync("https://x/?accessCode=A", CancellationToken.None);
        var partyAfterSet = fake.LastPresence!.Party;
        Assert.NotNull(partyAfterSet);

        // Simulate the lifecycle's per-account state update.
        await service.UpdateStateAsync(
            new RichPresenceState(PresenceMode.AccountsActive, 1, "Multi-clienting"),
            CancellationToken.None);

        // Party MUST still be present — preserved from the cache.
        Assert.NotNull(fake.LastPresence?.Party);
        Assert.Equal(partyAfterSet!.PartyId, fake.LastPresence!.Party!.PartyId);
        Assert.Equal(partyAfterSet.JoinSecret, fake.LastPresence.Party.JoinSecret);
    }

    [Fact]
    public async Task UpdateStateAsync_AfterClearPartyAsync_DoesNotResurrectParty()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);
        await service.SetPartyAsync("https://x/?accessCode=A", CancellationToken.None);
        await service.ClearPartyAsync(CancellationToken.None);
        Assert.Null(fake.LastPresence!.Party);

        await service.UpdateStateAsync(
            new RichPresenceState(PresenceMode.Idle, 0, null),
            CancellationToken.None);

        // Party stays cleared after explicit ClearPartyAsync, even across UpdateStateAsync.
        Assert.Null(fake.LastPresence!.Party);
    }

    [Fact]
    public async Task JoinRequested_DecodesCompactSecret_IntoLaunchableUrl()
    {
        var (service, fake) = StartedService(richPresenceEnabled: true);
        const string shareUrl = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId=42&accessCode=Z";
        var encoded = DiscordRichPresenceService.EncodeJoinSecret(shareUrl);

        string? captured = null;
        service.JoinRequested += (_, args) => captured = args.ServerShareUrl;

        fake.RaiseJoinRequested(encoded);

        Assert.NotNull(captured);
        Assert.Contains("placeId=42", captured);
        Assert.Contains("accessCode=Z", captured);
        Assert.Contains("PlaceLauncher.ashx", captured);
    }

    [Fact]
    public async Task JoinRequested_BadSecret_DoesNotThrow_DoesNotRaiseLaunchable()
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
    public void EncodeJoinSecret_FitsDiscord128Cap_AndRoundTripsPlaceAndCode()
    {
        // Lachee's Secrets.JoinSecret throws StringOutOfRangeException above 128 chars.
        // CHECKPOINT 1.8 fix: compact "P|placeId|linkCode|accessCode" format guarantees fit.
        const string url = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId=42&linkCode=XYZ1234567890";
        var encoded = DiscordRichPresenceService.EncodeJoinSecret(url);
        Assert.True(encoded.Length <= 128, $"Encoded JoinSecret length {encoded.Length} > 128.");

        var decoded = DiscordRichPresenceService.DecodeJoinSecret(encoded);
        // Decoded URL is reconstructed (different shape — no browserTrackerId, etc.) but
        // carries the same placeId + linkCode that the joining launcher needs.
        Assert.Contains("placeId=42", decoded);
        Assert.Contains("linkCode=XYZ1234567890", decoded);
        Assert.Contains("PlaceLauncher.ashx", decoded);
    }

    [Fact]
    public void EncodeJoinSecret_PublicGame_FitsAndDecodesToRequestGame()
    {
        const string url = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&placeId=920587237&isPlayTogetherGame=false";
        var encoded = DiscordRichPresenceService.EncodeJoinSecret(url);
        Assert.True(encoded.Length <= 128);

        var decoded = DiscordRichPresenceService.DecodeJoinSecret(encoded);
        Assert.Contains("placeId=920587237", decoded);
        Assert.Contains("request=RequestGame", decoded);
        Assert.Contains("isPlayTogetherGame=true", decoded); // decoder upgrades to true
    }

    [Fact]
    public void DecodeJoinSecret_UnknownFormat_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, DiscordRichPresenceService.DecodeJoinSecret("not_a_compact_secret"));
        Assert.Equal(string.Empty, DiscordRichPresenceService.DecodeJoinSecret(""));
        Assert.Equal(string.Empty, DiscordRichPresenceService.DecodeJoinSecret("P|||")); // missing placeId
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
        public event EventHandler<string>? Errored;
        public event EventHandler? PresenceUpdated;

        public void RaiseJoinRequested(string secret) => JoinRequested?.Invoke(this, secret);
        public void RaiseConnectionFailed() => ConnectionFailed?.Invoke(this, EventArgs.Empty);
        public void RaiseReady() => Ready?.Invoke(this, EventArgs.Empty);
        public void RaiseErrored(string message) => Errored?.Invoke(this, message);
        public void RaisePresenceUpdated() => PresenceUpdated?.Invoke(this, EventArgs.Empty);

        public void Dispose() => DisposeCalled = true;
    }
}
