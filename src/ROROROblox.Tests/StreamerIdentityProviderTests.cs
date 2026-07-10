using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

public class StreamerIdentityProviderTests
{
    private static StreamerIdentityProvider Make(bool active = false)
    {
        var settings = new FakeSettings(active);
        var provider = new StreamerIdentityProvider(
            new StreamerNamePool(new[] { "CaptainNoodle", "SirRerollington", "LadyPixel" }),
            new StreamerAvatarPool(new[] { "noodle", "duck", "potato" }),
            new InMemoryIdentityStore(),
            settings,
            persistAccount: (_, _) => Task.CompletedTask);
        provider.InitializeAsync(System.Array.Empty<(Guid, StreamerIdentity)>()).GetAwaiter().GetResult();
        return provider;
    }

    // Variant exposing the friend store + an account-persist recorder so friend-vs-account
    // routing can be asserted. Pools stay small (3) — callers that need reroll headroom build inline.
    private static (StreamerIdentityProvider provider, InMemoryIdentityStore friendStore, List<Guid> accountPersists) MakeWithProbes(bool active)
    {
        var friendStore = new InMemoryIdentityStore();
        var accountPersists = new List<Guid>();
        var provider = new StreamerIdentityProvider(
            new StreamerNamePool(new[] { "CaptainNoodle", "SirRerollington", "LadyPixel" }),
            new StreamerAvatarPool(new[] { "noodle", "duck", "potato" }),
            friendStore,
            new FakeSettings(active),
            persistAccount: (id, _) => { accountPersists.Add(id); return Task.CompletedTask; });
        provider.InitializeAsync(System.Array.Empty<(Guid, StreamerIdentity)>()).GetAwaiter().GetResult();
        return (provider, friendStore, accountPersists);
    }

    private static readonly Guid A = Guid.NewGuid();

    [Fact]
    public async Task Inactive_ReturnsRealIdentityVerbatim()
    {
        var p = Make(active: false);
        var id = p.ForAccount(A, "RealName", "https://real/avatar.png");
        Assert.Equal("RealName", id.Name);
        Assert.Equal("https://real/avatar.png", id.AvatarSource);
    }

    [Fact]
    public async Task Active_LeakScan_NeverReturnsRealNameAvatarOrUrl()
    {
        var p = Make(active: true);
        var id = p.ForAccount(A, "RealName", "https://real/avatar.png");
        Assert.NotEqual("RealName", id.Name);
        Assert.DoesNotContain("real", id.AvatarSource, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", id.AvatarSource, System.StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("pack://", id.AvatarSource);
    }

    [Fact]
    public async Task Active_SameAccount_StableAcrossCalls()
    {
        var p = Make(active: true);
        var first = p.ForAccount(A, "RealName", "x");
        var second = p.ForAccount(A, "RealName", "x");
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Reroll_ChangesTheIdentity_AndRaisesChanged()
    {
        var p = Make(active: true);
        var before = p.ForAccount(A, "RealName", "x");
        var raised = false; p.Changed += (_, _) => raised = true;
        await p.RerollAsync($"account:{A}");
        var after = p.ForAccount(A, "RealName", "x");
        Assert.True(raised);
        Assert.NotEqual(before.Name, after.Name);
    }

    [Fact]
    public async Task SetActive_PersistsToSettings_AndRaisesChanged()
    {
        var p = Make(active: false);
        var raised = false; p.Changed += (_, _) => raised = true;
        await p.SetActiveAsync(true);
        Assert.True(p.IsActive);
        Assert.True(raised);
    }

    [Fact]
    public async Task RerollAll_AcrossAccounts_AllDistinctAndChanged()
    {
        // Pools sized above the account count (6 names/avatars for 3 accounts) so a reroll has
        // room to land a genuinely different identity — a saturated 3-name pool can't guarantee change.
        var provider = new StreamerIdentityProvider(
            new StreamerNamePool(new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot" }),
            new StreamerAvatarPool(new[] { "a", "b", "c", "d", "e", "f" }),
            new InMemoryIdentityStore(),
            new FakeSettings(on: true),
            persistAccount: (_, _) => Task.CompletedTask);
        await provider.InitializeAsync(System.Array.Empty<(Guid, StreamerIdentity)>());

        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var before = ids.Select(g => provider.ForAccount(g, "RealName", "x").Name).ToArray();

        var raised = false; provider.Changed += (_, _) => raised = true;
        await provider.RerollAllAsync();

        var after = ids.Select(g => provider.ForAccount(g, "RealName", "x").Name).ToArray();

        Assert.True(raised);
        Assert.Equal(3, after.Distinct().Count());       // no two accounts collide on one fake name
        for (var i = 0; i < ids.Length; i++)
            Assert.NotEqual(before[i], after[i]);          // every account's identity actually changed
    }

    [Fact]
    public async Task Inactive_ForFriend_ReturnsRealIdentityVerbatim()
    {
        var (p, _, _) = MakeWithProbes(active: false);
        var id = p.ForFriend(12345L, "FriendReal", "https://real/friend.png");
        Assert.Equal("FriendReal", id.Name);
        Assert.Equal("https://real/friend.png", id.AvatarSource);
    }

    [Fact]
    public async Task Active_ForFriend_LeakScan_NeverReturnsRealNameAvatarOrUrl()
    {
        var (p, _, _) = MakeWithProbes(active: true);
        var id = p.ForFriend(12345L, "FriendReal", "https://real/friend.png");
        Assert.NotEqual("FriendReal", id.Name);
        Assert.DoesNotContain("real", id.AvatarSource, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", id.AvatarSource, System.StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("pack://", id.AvatarSource);
    }

    [Fact]
    public async Task Active_SameFriend_StableAcrossCalls()
    {
        var (p, _, _) = MakeWithProbes(active: true);
        var first = p.ForFriend(12345L, "FriendReal", "x");
        var second = p.ForFriend(12345L, "FriendReal", "x");
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Active_ForFriend_PersistsViaFriendStore_NotAccountCallback()
    {
        var (p, friendStore, accountPersists) = MakeWithProbes(active: true);
        p.ForFriend(12345L, "FriendReal", "x");

        var saved = await friendStore.LoadAllAsync();
        Assert.True(saved.ContainsKey("friend:12345"));    // routed to the friend store under the friend key
        Assert.Empty(accountPersists);                     // never routed through the account persist callback
    }

    private sealed class FakeSettings : IAppSettings
    {
        private bool _on;
        public FakeSettings(bool on) => _on = on;
        public Task<bool> GetStreamerModeAsync() => Task.FromResult(_on);
        public Task SetStreamerModeAsync(bool on) { _on = on; return Task.CompletedTask; }
        // Remaining IAppSettings members throw NotImplementedException — not exercised here.
        public Task<string?> GetDefaultPlaceUrlAsync() => throw new NotImplementedException();
        public Task SetDefaultPlaceUrlAsync(string url) => throw new NotImplementedException();
        public Task<bool> GetLaunchMainOnStartupAsync() => throw new NotImplementedException();
        public Task SetLaunchMainOnStartupAsync(bool e) => throw new NotImplementedException();
        public Task<string?> GetActiveThemeIdAsync() => throw new NotImplementedException();
        public Task SetActiveThemeIdAsync(string t) => throw new NotImplementedException();
        public Task<bool> GetBloxstrapWarningDismissedAsync() => throw new NotImplementedException();
        public Task SetBloxstrapWarningDismissedAsync(bool v) => throw new NotImplementedException();
        public Task<bool> GetMuteIdleAlertsAsync() => throw new NotImplementedException();
        public Task SetMuteIdleAlertsAsync(bool m) => throw new NotImplementedException();
        public Task<int> GetIdleWarnThresholdMinutesAsync() => throw new NotImplementedException();
        public Task SetIdleWarnThresholdMinutesAsync(int m) => throw new NotImplementedException();
        public Task<bool> GetCarefulSquadLaunchAsync() => throw new NotImplementedException();
        public Task SetCarefulSquadLaunchAsync(bool c) => throw new NotImplementedException();
    }

    private sealed class InMemoryIdentityStore : IStreamerIdentityStore
    {
        private readonly Dictionary<string, StreamerIdentity> _m = new();
        public Task<IReadOnlyDictionary<string, StreamerIdentity>> LoadAllAsync()
            => Task.FromResult<IReadOnlyDictionary<string, StreamerIdentity>>(_m);
        public Task SaveAsync(string key, StreamerIdentity id) { _m[key] = id; return Task.CompletedTask; }
    }
}
