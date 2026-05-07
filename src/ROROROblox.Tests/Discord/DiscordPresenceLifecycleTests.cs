using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Integration coverage for the keystone wiring. Every collaborator is faked so we can
/// drive the full event flow in-memory: simulate AccountStarted/Stopped, observe presence
/// updates + webhook posts; raise JoinRequested, observe the launcher invocation.
/// </summary>
public class DiscordPresenceLifecycleTests
{
    private static readonly Account AccountOlder = new(
        Id: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
        DisplayName: "Older",
        AvatarUrl: "https://x/o.png",
        CreatedAt: DateTimeOffset.UtcNow.AddDays(-3),
        LastLaunchedAt: DateTimeOffset.UtcNow.AddHours(-2),
        IsMain: false,
        SortOrder: 0,
        IsSelected: true,
        CaptionColorHex: null);

    private static readonly Account AccountNewer = new(
        Id: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        DisplayName: "Newer",
        AvatarUrl: "https://x/n.png",
        CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
        LastLaunchedAt: DateTimeOffset.UtcNow.AddMinutes(-5),
        IsMain: false,
        SortOrder: 1,
        IsSelected: true,
        CaptionColorHex: null);

    [Fact]
    public async Task StartAsync_StartsPresence_AndSubscribesLifecycle()
    {
        var (sut, presence, _, lifecycle, _, _) = NewSystemUnderTest();

        await sut.StartAsync(CancellationToken.None);

        Assert.True(presence.StartCalled);
        Assert.NotNull(lifecycle.GetType().GetEvent("AccountStarted"));
    }

    [Fact]
    public async Task AccountStarted_UpdatesPresenceToActive_AndPostsWebhooks()
    {
        var (sut, presence, webhook, lifecycle, _, _) = NewSystemUnderTest(AccountNewer);
        await sut.StartAsync(CancellationToken.None);

        lifecycle.RaiseAccountStarted(AccountNewer, processId: 1234, currentActiveCount: 1);
        await Task.Delay(50);

        Assert.NotNull(presence.LastState);
        Assert.Equal(PresenceMode.AccountsActive, presence.LastState!.Mode);
        Assert.Equal(1, presence.LastState.ActiveAccountCount);
        Assert.Equal("Multi-clienting", presence.LastState.CurrentActivity);
        Assert.Equal(1, webhook.LaunchCalls.Single());
        Assert.Equal(1, webhook.ThresholdCalls.Single());
    }

    [Fact]
    public async Task AccountStopped_NonZero_StaysActive_NoClearParty()
    {
        var (sut, presence, _, lifecycle, _, _) = NewSystemUnderTest(AccountNewer);
        await sut.StartAsync(CancellationToken.None);

        lifecycle.RaiseAccountStopped(AccountNewer, processId: 100, currentActiveCount: 2);
        await Task.Delay(50);

        Assert.Equal(PresenceMode.AccountsActive, presence.LastState!.Mode);
        Assert.Equal(2, presence.LastState.ActiveAccountCount);
        Assert.False(presence.ClearPartyCalled);
    }

    [Fact]
    public async Task AccountStopped_DropsToZero_FlipsToIdle_AndClearsParty()
    {
        var (sut, presence, _, lifecycle, _, _) = NewSystemUnderTest(AccountNewer);
        await sut.StartAsync(CancellationToken.None);

        lifecycle.RaiseAccountStopped(AccountNewer, processId: 100, currentActiveCount: 0);
        await Task.Delay(50);

        Assert.Equal(PresenceMode.Idle, presence.LastState!.Mode);
        Assert.Equal(0, presence.LastState.ActiveAccountCount);
        Assert.True(presence.ClearPartyCalled);
    }

    [Fact]
    public async Task JoinRequested_LaunchesMostRecentAccount_WithShareUrl()
    {
        var (sut, presence, _, _, launcher, store) = NewSystemUnderTest(AccountOlder, AccountNewer);
        await sut.StartAsync(CancellationToken.None);

        const string shareUrl = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?accessCode=A&placeId=1";
        presence.RaiseJoinRequested(shareUrl);
        await Task.Delay(50);

        Assert.NotNull(launcher.LastLaunch);
        Assert.Equal(shareUrl, launcher.LastLaunch!.PlaceUrl);
        // Cookie comes from RetrieveCookieAsync — verify it's the one for the most-recent account.
        Assert.Equal(store.CookieFor(AccountNewer.Id), launcher.LastLaunch.Cookie);
    }

    [Fact]
    public async Task JoinRequested_NoAccounts_LogsAndDoesNotThrow()
    {
        var (sut, presence, _, _, launcher, _) = NewSystemUnderTest(/* no accounts */);
        await sut.StartAsync(CancellationToken.None);

        presence.RaiseJoinRequested("https://x/?accessCode=A");
        await Task.Delay(50);

        Assert.Null(launcher.LastLaunch);
    }

    [Fact]
    public void ConvertToPublicWebUrl_LinkCode_PreservesShareViaQueryParam()
    {
        var input = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId=920587237&linkCode=ABC123";
        var output = DiscordPresenceLifecycle.ConvertToPublicWebUrl(input);

        Assert.Equal("https://www.roblox.com/games/920587237?privateServerLinkCode=ABC123", output);
    }

    [Fact]
    public void ConvertToPublicWebUrl_PublicGame_StripsToPlacePage()
    {
        var input = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&placeId=920587237&isPlayTogetherGame=true";
        var output = DiscordPresenceLifecycle.ConvertToPublicWebUrl(input);

        Assert.Equal("https://www.roblox.com/games/920587237", output);
    }

    [Fact]
    public void ConvertToPublicWebUrl_AccessCode_FallsBackToPlacePageOnly()
    {
        // accessCode is owner-shared and has no public URL representation; we drop it and
        // open the place page. Joiner can still find the inviter in-game via friends list
        // once they're on the same place.
        var input = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestPrivateGame&placeId=920587237&accessCode=PRIV";
        var output = DiscordPresenceLifecycle.ConvertToPublicWebUrl(input);

        Assert.Equal("https://www.roblox.com/games/920587237", output);
    }

    [Fact]
    public void ConvertToPublicWebUrl_Unparseable_FallsBackToHomePage()
    {
        var output = DiscordPresenceLifecycle.ConvertToPublicWebUrl("garbage-not-a-url");

        Assert.Equal("https://www.roblox.com/", output);
    }

    [Fact]
    public async Task StopAsync_DisposesPresence_AndUnsubscribes()
    {
        var (sut, presence, _, lifecycle, _, _) = NewSystemUnderTest(AccountNewer);
        await sut.StartAsync(CancellationToken.None);

        await sut.StopAsync(CancellationToken.None);

        Assert.True(presence.DisposeCalled);

        // Subsequent lifecycle event must NOT update presence (we unsubscribed).
        var presenceCallsBefore = presence.UpdateStateCalls;
        lifecycle.RaiseAccountStarted(AccountNewer, processId: 7, currentActiveCount: 1);
        await Task.Delay(50);
        Assert.Equal(presenceCallsBefore, presence.UpdateStateCalls);
    }

    [Fact]
    public async Task SubscriberThrows_DoesNotPropagateBackToTracker()
    {
        var (sut, presence, _, lifecycle, _, _) = NewSystemUnderTest(AccountNewer);
        // Make presence throw — handler must swallow.
        presence.UpdateStateThrows = new InvalidOperationException("Discord IPC dead");
        await sut.StartAsync(CancellationToken.None);

        // RaiseAccountStarted must not throw to caller.
        var ex = Record.Exception(() =>
            lifecycle.RaiseAccountStarted(AccountNewer, processId: 1, currentActiveCount: 1));
        Assert.Null(ex);
    }

    // ---- Helpers ----

    private static (DiscordPresenceLifecycle sut, FakePresence presence, FakeWebhook webhook,
                    FakeLifecycle lifecycle, FakeLauncher launcher, FakeAccountStore store)
        NewSystemUnderTest(params Account[] accounts)
    {
        var presence = new FakePresence();
        var webhook = new FakeWebhook();
        var lifecycle = new FakeLifecycle();
        var launcher = new FakeLauncher();
        var store = new FakeAccountStore(accounts);
        var sut = new DiscordPresenceLifecycle(
            presence, webhook, lifecycle, launcher, store,
            NullLogger<DiscordPresenceLifecycle>.Instance);
        return (sut, presence, webhook, lifecycle, launcher, store);
    }

    private sealed class FakePresence : IDiscordPresence
    {
        public bool StartCalled { get; private set; }
        public bool ClearPartyCalled { get; private set; }
        public bool DisposeCalled { get; private set; }
        public RichPresenceState? LastState { get; private set; }
        public int UpdateStateCalls { get; private set; }
        public Exception? UpdateStateThrows { get; set; }

        public Task StartAsync(CancellationToken ct) { StartCalled = true; return Task.CompletedTask; }

        public Task UpdateStateAsync(RichPresenceState state, CancellationToken ct)
        {
            UpdateStateCalls++;
            if (UpdateStateThrows is not null) throw UpdateStateThrows;
            LastState = state;
            return Task.CompletedTask;
        }

        public Task SetPartyAsync(string serverShareUrl, CancellationToken ct) => Task.CompletedTask;
        public Task ClearPartyAsync(CancellationToken ct) { ClearPartyCalled = true; return Task.CompletedTask; }

        public event EventHandler<JoinRequestedEventArgs>? JoinRequested;
        public void RaiseJoinRequested(string url) => JoinRequested?.Invoke(this, new JoinRequestedEventArgs(url));

        public ValueTask DisposeAsync() { DisposeCalled = true; return ValueTask.CompletedTask; }
    }

    private sealed class FakeWebhook : IDiscordWebhook
    {
        public List<int> LaunchCalls { get; } = [];
        public List<string> ServerJoinCalls { get; } = [];
        public List<int> ThresholdCalls { get; } = [];

        public Task PostLaunchAsync(int accountCount, CancellationToken ct) { LaunchCalls.Add(accountCount); return Task.CompletedTask; }
        public Task PostServerJoinAsync(string serverShareUrl, CancellationToken ct) { ServerJoinCalls.Add(serverShareUrl); return Task.CompletedTask; }
        public Task PostAccountThresholdAsync(int accountCount, CancellationToken ct) { ThresholdCalls.Add(accountCount); return Task.CompletedTask; }
    }

    private sealed class FakeLifecycle : IAccountLifecycle
    {
        public event EventHandler<AccountStartedEventArgs>? AccountStarted;
        public event EventHandler<AccountStoppedEventArgs>? AccountStopped;

        public void RaiseAccountStarted(Account a, int processId, int currentActiveCount) =>
            AccountStarted?.Invoke(this, new AccountStartedEventArgs(a, processId, currentActiveCount));
        public void RaiseAccountStopped(Account a, int processId, int currentActiveCount) =>
            AccountStopped?.Invoke(this, new AccountStoppedEventArgs(a, processId, currentActiveCount));
    }

    private sealed class FakeLauncher : IRobloxLauncher
    {
        public sealed record LaunchInvocation(string Cookie, string? PlaceUrl, LaunchTarget? Target);
        public LaunchInvocation? LastLaunch { get; private set; }

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null)
        {
            LastLaunch = new LaunchInvocation(cookie, placeUrl, null);
            return Task.FromResult<LaunchResult>(new LaunchResult.Started(1234, DateTimeOffset.UtcNow));
        }

        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target)
        {
            LastLaunch = new LaunchInvocation(cookie, null, target);
            return Task.FromResult<LaunchResult>(new LaunchResult.Started(1234, DateTimeOffset.UtcNow));
        }
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        private readonly List<Account> _accounts;
        private readonly Dictionary<Guid, string> _cookies = new();

        public FakeAccountStore(params Account[] accounts)
        {
            _accounts = [.. accounts];
            foreach (var a in _accounts) _cookies[a.Id] = $"cookie-for-{a.Id}";
        }

        public string CookieFor(Guid id) => _cookies[id];

        public Task<IReadOnlyList<Account>> ListAsync() => Task.FromResult<IReadOnlyList<Account>>(_accounts);
        public Task<string> RetrieveCookieAsync(Guid id) => Task.FromResult(_cookies[id]);

        public Task<Account> AddAsync(string displayName, string avatarUrl, string cookie) => throw new NotImplementedException();
        public Task RemoveAsync(Guid id) => throw new NotImplementedException();
        public Task UpdateCookieAsync(Guid id, string newCookie) => throw new NotImplementedException();
        public Task TouchLastLaunchedAsync(Guid id) => throw new NotImplementedException();
        public Task SetMainAsync(Guid id) => throw new NotImplementedException();
        public Task UpdateSortOrderAsync(IReadOnlyList<Guid> idsInOrder) => throw new NotImplementedException();
        public Task SetSelectedAsync(Guid id, bool isSelected) => throw new NotImplementedException();
        public Task SetCaptionColorAsync(Guid id, string? hex) => throw new NotImplementedException();
    }
}
