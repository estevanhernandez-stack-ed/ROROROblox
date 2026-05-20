using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// v1.5.0 tests for <see cref="PresenceService"/> (checklist item 1). Hand-rolled fakes for
/// <see cref="IRobloxApi"/> and <see cref="IAccountStore"/> (zero new dependencies — same pattern
/// as <see cref="AccountUserIdBackfillServiceTests"/>). Spec §1 (PresenceService).
///
/// Item-1 scope is the poll loop + presence→event mapping + game-name cache. Resilience
/// (401/429/hold-last) and the fast-confirm re-poll are item 2 — not covered here. Tests call
/// <see cref="PresenceService.PollOnceAsync"/> directly rather than driving the timer.
///
/// Cases:
/// 1. InGame with a known PlaceId → event raised, GameName = the metadata name.
/// 2. Cache hit: two polls for the same PlaceId → GetGameMetadataByPlaceIdAsync called once.
/// 3. Offline / OnlineWebsite → event raised with GameName null.
/// 4. Multiple targets in one pass → one event per target.
/// </summary>
public class PresenceServiceTests
{
    private const string Cookie = "FAKE_COOKIE_FOR_TESTS_ONLY";

    private static PresenceService CreateService(
        FakeRobloxApi api,
        FakeAccountStore store,
        IReadOnlyList<PresenceTarget> targets) =>
        new(api, store, () => targets, NullLogger<PresenceService>.Instance);

    [Fact]
    public async Task PollOnceAsync_InGameWithKnownPlace_RaisesEventWithResolvedGameName()
    {
        var accountId = Guid.NewGuid();
        const long userId = 101;
        const long placeId = 920587237;
        var store = new FakeAccountStore { CookieByAccount = { [accountId] = Cookie } };
        var api = new FakeRobloxApi
        {
            PresenceByCookie = { [Cookie] = [new UserPresence(userId, UserPresenceType.InGame, placeId, "job-1", "Adopt Me!")] },
            GameNameByPlaceId = { [placeId] = "Adopt Me!" },
        };
        var service = CreateService(api, store, [new PresenceTarget(accountId, userId)]);

        var events = new List<AccountPresenceEventArgs>();
        service.AccountPresenceUpdated += (_, e) => events.Add(e);

        await service.PollOnceAsync();

        var ev = Assert.Single(events);
        Assert.Equal(accountId, ev.AccountId);
        Assert.Equal(UserPresenceType.InGame, ev.PresenceType);
        Assert.Equal(placeId, ev.PlaceId);
        Assert.Equal("Adopt Me!", ev.GameName);
    }

    [Fact]
    public async Task PollOnceAsync_TwiceSamePlace_ResolvesGameNameOnlyOnce_CacheHit()
    {
        var accountId = Guid.NewGuid();
        const long userId = 202;
        const long placeId = 606849621;
        var store = new FakeAccountStore { CookieByAccount = { [accountId] = Cookie } };
        var api = new FakeRobloxApi
        {
            PresenceByCookie = { [Cookie] = [new UserPresence(userId, UserPresenceType.InGame, placeId, "job-2", "Jailbreak")] },
            GameNameByPlaceId = { [placeId] = "Jailbreak" },
        };
        var service = CreateService(api, store, [new PresenceTarget(accountId, userId)]);

        var events = new List<AccountPresenceEventArgs>();
        service.AccountPresenceUpdated += (_, e) => events.Add(e);

        await service.PollOnceAsync();
        await service.PollOnceAsync();

        // Two polls → two events, both naming the game.
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("Jailbreak", e.GameName));
        // The cache must spare the second metadata fetch.
        Assert.Equal(1, api.GetGameMetadataCalls);
        Assert.Equal([placeId], api.GameMetadataPlaceIds);
    }

    [Fact]
    public async Task PollOnceAsync_OfflineAndOnlineWebsite_RaiseEventWithNullGameName()
    {
        var offlineId = Guid.NewGuid();
        var onlineId = Guid.NewGuid();
        const long offlineUser = 11;
        const long onlineUser = 22;
        var store = new FakeAccountStore
        {
            CookieByAccount = { [offlineId] = "cookie-off", [onlineId] = "cookie-on" },
        };
        var api = new FakeRobloxApi
        {
            PresenceByCookie =
            {
                ["cookie-off"] = [new UserPresence(offlineUser, UserPresenceType.Offline, null, null, null)],
                ["cookie-on"] = [new UserPresence(onlineUser, UserPresenceType.OnlineWebsite, null, null, null)],
            },
        };
        var service = CreateService(api, store,
        [
            new PresenceTarget(offlineId, offlineUser),
            new PresenceTarget(onlineId, onlineUser),
        ]);

        var events = new List<AccountPresenceEventArgs>();
        service.AccountPresenceUpdated += (_, e) => events.Add(e);

        await service.PollOnceAsync();

        Assert.Equal(2, events.Count);
        var offlineEv = Assert.Single(events, e => e.AccountId == offlineId);
        Assert.Equal(UserPresenceType.Offline, offlineEv.PresenceType);
        Assert.Null(offlineEv.GameName);
        var onlineEv = Assert.Single(events, e => e.AccountId == onlineId);
        Assert.Equal(UserPresenceType.OnlineWebsite, onlineEv.PresenceType);
        Assert.Null(onlineEv.GameName);
        // Never a metadata lookup for not-in-game presence.
        Assert.Equal(0, api.GetGameMetadataCalls);
    }

    [Fact]
    public async Task PollOnceAsync_MultipleTargets_RaisesOneEventPerTarget()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var store = new FakeAccountStore
        {
            CookieByAccount = { [id1] = "c1", [id2] = "c2", [id3] = "c3" },
        };
        var api = new FakeRobloxApi
        {
            PresenceByCookie =
            {
                ["c1"] = [new UserPresence(1, UserPresenceType.InGame, 5000, "j1", "Game A")],
                ["c2"] = [new UserPresence(2, UserPresenceType.Offline, null, null, null)],
                ["c3"] = [new UserPresence(3, UserPresenceType.OnlineWebsite, null, null, null)],
            },
            GameNameByPlaceId = { [5000] = "Game A" },
        };
        var service = CreateService(api, store,
        [
            new PresenceTarget(id1, 1),
            new PresenceTarget(id2, 2),
            new PresenceTarget(id3, 3),
        ]);

        var events = new List<AccountPresenceEventArgs>();
        service.AccountPresenceUpdated += (_, e) => events.Add(e);

        await service.PollOnceAsync();

        Assert.Equal(3, events.Count);
        Assert.Contains(events, e => e.AccountId == id1 && e.GameName == "Game A");
        Assert.Contains(events, e => e.AccountId == id2 && e.GameName is null);
        Assert.Contains(events, e => e.AccountId == id3 && e.GameName is null);
    }

    // ---- fakes ----

    private sealed class FakeAccountStore : IAccountStore
    {
        public Dictionary<Guid, string> CookieByAccount { get; } = [];

        public Task<string> RetrieveCookieAsync(Guid id) =>
            CookieByAccount.TryGetValue(id, out var c)
                ? Task.FromResult(c)
                : throw new InvalidOperationException($"FakeAccountStore: no cookie for {id}");

        // Unused by PresenceService — throw to surface accidental use.
        public Task<IReadOnlyList<Account>> ListAsync() => throw new NotImplementedException();
        public Task<Account> AddAsync(string displayName, string avatarUrl, string cookie) => throw new NotImplementedException();
        public Task RemoveAsync(Guid id) => throw new NotImplementedException();
        public Task UpdateCookieAsync(Guid id, string newCookie) => throw new NotImplementedException();
        public Task TouchLastLaunchedAsync(Guid id) => throw new NotImplementedException();
        public Task SetMainAsync(Guid id) => throw new NotImplementedException();
        public Task UpdateSortOrderAsync(IReadOnlyList<Guid> idsInOrder) => throw new NotImplementedException();
        public Task SetSelectedAsync(Guid id, bool isSelected) => throw new NotImplementedException();
        public Task SetCaptionColorAsync(Guid id, string? hex) => throw new NotImplementedException();
        public Task SetFpsCapAsync(Guid id, int? fps) => throw new NotImplementedException();
        public Task UpdateLocalNameAsync(Guid accountId, string? localName) => throw new NotImplementedException();
        public Task UpdateRobloxUserIdAsync(Guid accountId, long userId) => throw new NotImplementedException();
    }

    private sealed class FakeRobloxApi : IRobloxApi
    {
        public Dictionary<string, IReadOnlyList<UserPresence>> PresenceByCookie { get; } = [];
        public Dictionary<long, string> GameNameByPlaceId { get; } = [];
        public int GetGameMetadataCalls { get; private set; }
        public List<long> GameMetadataPlaceIds { get; } = [];

        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) =>
            PresenceByCookie.TryGetValue(cookie, out var p)
                ? Task.FromResult(p)
                : throw new InvalidOperationException($"FakeRobloxApi: no presence for cookie '{cookie}'");

        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId)
        {
            GetGameMetadataCalls++;
            GameMetadataPlaceIds.Add(placeId);
            return GameNameByPlaceId.TryGetValue(placeId, out var name)
                ? Task.FromResult<GameMetadata?>(new GameMetadata(placeId, 0, name, ""))
                : Task.FromResult<GameMetadata?>(null);
        }

        // Unused — throw to surface accidental use.
        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotImplementedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotImplementedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotImplementedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotImplementedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotImplementedException();
    }
}
