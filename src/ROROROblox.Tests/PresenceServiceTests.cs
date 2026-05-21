using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// v1.5.0 tests for <see cref="PresenceService"/> (checklist items 1 + 2). Hand-rolled fakes for
/// <see cref="IRobloxApi"/> and <see cref="IAccountStore"/> (zero new dependencies — same pattern
/// as <see cref="AccountUserIdBackfillServiceTests"/>). Spec §1 (PresenceService).
///
/// Item-1 scope is the poll loop + presence→event mapping + game-name cache.
///
/// Item-2 scope (this round) is resilience + the fast-confirm re-poll:
/// - 401 / <see cref="CookieExpiredException"/> → raise <see cref="PresenceService.AccountSessionExpired"/>
///   (NOT a presence event) so the row flips to "Session expired."
/// - Empty list returned by the api (call failed — 429, network, malformed) → HOLD last-known:
///   raise nothing.
/// - Populated list with <see cref="UserPresenceType.Offline"/> → raise the presence event with
///   Offline (genuinely-offline is distinct from a failed poll).
/// - Concurrency cap (SemaphoreSlim ≤ 4) + jitter so N accounts don't fire simultaneously.
/// - <see cref="PresenceService.RequestImmediateRefreshAsync"/> → poll exactly the one account
///   that's in the snapshot; no-op for an id absent from the snapshot.
///
/// Tests call <see cref="PresenceService.PollOnceAsync"/> /
/// <see cref="PresenceService.RequestImmediateRefreshAsync"/> directly rather than driving the timer.
///
/// Cases:
/// 1. InGame with a known PlaceId → event raised, GameName = the metadata name.
/// 2. Cache hit: two polls for the same PlaceId → GetGameMetadataByPlaceIdAsync called once.
/// 3. Offline / OnlineWebsite → event raised with GameName null.
/// 4. Multiple targets in one pass → one event per target.
/// 5. CookieExpiredException → AccountSessionExpired raised, no presence event (item 2).
/// 6. Empty list → no event of any kind, hold last-known (item 2).
/// 7. Populated Offline list → presence event raised with Offline (distinct from #6) (item 2).
/// 8. RequestImmediateRefreshAsync(id-in-snapshot) → exactly one presence call + event (item 2).
/// 9. RequestImmediateRefreshAsync(id-not-in-snapshot) → no api call, no throw (item 2).
/// 10. Concurrency cap respected — max in-flight presence calls ≤ 4 (item 2).
/// </summary>
public class PresenceServiceTests
{
    private const string Cookie = "FAKE_COOKIE_FOR_TESTS_ONLY";

    // Zero jitter in tests so timing is deterministic — the real (random) jitter is exercised
    // by the default constructor path in production; the cap is what we assert here.
    private static readonly TimeSpan NoJitter = TimeSpan.Zero;

    private static PresenceService CreateService(
        FakeRobloxApi api,
        FakeAccountStore store,
        IReadOnlyList<PresenceTarget> targets) =>
        new(api, store, () => targets, NullLogger<PresenceService>.Instance,
            pollInterval: null, maxJitter: NoJitter);

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

    // ---- item 2: resilience ----

    [Fact]
    public async Task PollOnceAsync_CookieExpired_RaisesSessionExpired_NoPresenceEvent()
    {
        var accountId = Guid.NewGuid();
        const long userId = 303;
        var store = new FakeAccountStore { CookieByAccount = { [accountId] = Cookie } };
        var api = new FakeRobloxApi { ThrowExpiredForCookie = { Cookie } };
        var service = CreateService(api, store, [new PresenceTarget(accountId, userId)]);

        var presenceEvents = new List<AccountPresenceEventArgs>();
        var expiredIds = new List<Guid>();
        service.AccountPresenceUpdated += (_, e) => presenceEvents.Add(e);
        service.AccountSessionExpired += (_, id) => expiredIds.Add(id);

        await service.PollOnceAsync();

        // The 401 path flips the row to session-expired and raises NO presence event.
        Assert.Empty(presenceEvents);
        var raisedId = Assert.Single(expiredIds);
        Assert.Equal(accountId, raisedId);
    }

    [Fact]
    public async Task PollOnceAsync_EmptyList_HoldsLastKnown_RaisesNothing()
    {
        var accountId = Guid.NewGuid();
        const long userId = 404;
        var store = new FakeAccountStore { CookieByAccount = { [accountId] = Cookie } };
        // Empty list means the call FAILED (429/network/malformed) — never a presence event.
        var api = new FakeRobloxApi { PresenceByCookie = { [Cookie] = [] } };
        var service = CreateService(api, store, [new PresenceTarget(accountId, userId)]);

        var presenceEvents = new List<AccountPresenceEventArgs>();
        var expiredIds = new List<Guid>();
        service.AccountPresenceUpdated += (_, e) => presenceEvents.Add(e);
        service.AccountSessionExpired += (_, id) => expiredIds.Add(id);

        await service.PollOnceAsync();

        Assert.Empty(presenceEvents);
        Assert.Empty(expiredIds);
    }

    [Fact]
    public async Task PollOnceAsync_PopulatedOffline_RaisesPresenceEventWithOffline()
    {
        // The distinction that matters: a populated list whose single entry is Offline means the
        // account is GENUINELY offline → raise Offline. (Contrast PollOnceAsync_EmptyList above:
        // empty = the call failed, hold last-known.)
        var accountId = Guid.NewGuid();
        const long userId = 505;
        var store = new FakeAccountStore { CookieByAccount = { [accountId] = Cookie } };
        var api = new FakeRobloxApi
        {
            PresenceByCookie = { [Cookie] = [new UserPresence(userId, UserPresenceType.Offline, null, null, null)] },
        };
        var service = CreateService(api, store, [new PresenceTarget(accountId, userId)]);

        var presenceEvents = new List<AccountPresenceEventArgs>();
        var expiredIds = new List<Guid>();
        service.AccountPresenceUpdated += (_, e) => presenceEvents.Add(e);
        service.AccountSessionExpired += (_, id) => expiredIds.Add(id);

        await service.PollOnceAsync();

        var ev = Assert.Single(presenceEvents);
        Assert.Equal(accountId, ev.AccountId);
        Assert.Equal(UserPresenceType.Offline, ev.PresenceType);
        Assert.Null(ev.GameName);
        Assert.Empty(expiredIds);
    }

    [Fact]
    public async Task RequestImmediateRefreshAsync_IdInSnapshot_PollsExactlyThatOneAccount()
    {
        var targetId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        const long targetUser = 606;
        const long otherUser = 707;
        var store = new FakeAccountStore
        {
            CookieByAccount = { [targetId] = "cookie-target", [otherId] = "cookie-other" },
        };
        var api = new FakeRobloxApi
        {
            PresenceByCookie =
            {
                ["cookie-target"] = [new UserPresence(targetUser, UserPresenceType.OnlineWebsite, null, null, null)],
                ["cookie-other"] = [new UserPresence(otherUser, UserPresenceType.InGame, 5000, "j", "Game")],
            },
        };
        var service = CreateService(api, store,
        [
            new PresenceTarget(targetId, targetUser),
            new PresenceTarget(otherId, otherUser),
        ]);

        var events = new List<AccountPresenceEventArgs>();
        service.AccountPresenceUpdated += (_, e) => events.Add(e);

        await service.RequestImmediateRefreshAsync(targetId);

        // Exactly ONE presence call, for the target's cookie only — the other account is untouched.
        Assert.Equal(1, api.GetPresenceCalls);
        Assert.Equal(["cookie-target"], api.PresenceCookiesQueried);
        var ev = Assert.Single(events);
        Assert.Equal(targetId, ev.AccountId);
    }

    [Fact]
    public async Task RequestImmediateRefreshAsync_IdNotInSnapshot_NoApiCall_NoThrow()
    {
        var inSnapshotId = Guid.NewGuid();
        var missingId = Guid.NewGuid(); // expired / no userId → absent from the snapshot
        var store = new FakeAccountStore { CookieByAccount = { [inSnapshotId] = Cookie } };
        var api = new FakeRobloxApi
        {
            PresenceByCookie = { [Cookie] = [new UserPresence(1, UserPresenceType.OnlineWebsite, null, null, null)] },
        };
        var service = CreateService(api, store, [new PresenceTarget(inSnapshotId, 1)]);

        var events = new List<AccountPresenceEventArgs>();
        service.AccountPresenceUpdated += (_, e) => events.Add(e);

        await service.RequestImmediateRefreshAsync(missingId); // no throw

        Assert.Equal(0, api.GetPresenceCalls);
        Assert.Empty(events);
    }

    [Fact]
    public async Task PollOnceAsync_ManyTargets_RespectsConcurrencyCapOfFour()
    {
        // 12 accounts, each presence call gated on a release signal so we can observe how many
        // are in flight at once. The cap is 4 — never more should be mid-call simultaneously.
        const int accountCount = 12;
        const int cap = 4;
        var store = new FakeAccountStore();
        var api = new FakeRobloxApi { GateConcurrentCalls = true };
        var targets = new List<PresenceTarget>();
        for (var i = 0; i < accountCount; i++)
        {
            var id = Guid.NewGuid();
            var cookie = $"cookie-{i}";
            store.CookieByAccount[id] = cookie;
            api.PresenceByCookie[cookie] = [new UserPresence(i + 1, UserPresenceType.OnlineWebsite, null, null, null)];
            targets.Add(new PresenceTarget(id, i + 1));
        }

        var service = CreateService(api, store, targets);

        var pollTask = service.PollOnceAsync();

        // Let the in-flight count settle, then release everyone. The gate holds calls open so the
        // counter reflects true simultaneity rather than racing to completion.
        await api.WaitForCallsToParkAsync(cap);
        api.ReleaseGate();
        await pollTask;

        // Never MORE than the cap (the politeness guarantee)...
        Assert.True(api.MaxConcurrentCalls <= cap,
            $"Expected at most {cap} concurrent presence calls; observed {api.MaxConcurrentCalls}.");
        // ...and with 12 > 4 accounts it must actually REACH the cap — otherwise a purely serial
        // implementation (max-in-flight = 1) would satisfy "≤ 4" without proving the cap exists.
        Assert.Equal(cap, api.MaxConcurrentCalls);
        Assert.Equal(accountCount, api.GetPresenceCalls);
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
        public Task SetTagsAsync(Guid id, IReadOnlyList<string> tags) => throw new NotImplementedException();
        public Task<AccountExportResult> ExportAccountsAsync(IEnumerable<Guid> ids) => throw new NotImplementedException();
        public Task<ImportMergeResult> ImportMergeAsync(IReadOnlyList<ROROROblox.Core.Transport.AccountExportRecord> records) => throw new NotImplementedException();
    }

    private sealed class FakeRobloxApi : IRobloxApi
    {
        private readonly object _sync = new();
        private int _inFlight;
        private readonly TaskCompletionSource _gate =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Dictionary<string, IReadOnlyList<UserPresence>> PresenceByCookie { get; } = [];
        public Dictionary<long, string> GameNameByPlaceId { get; } = [];
        public int GetGameMetadataCalls { get; private set; }
        public List<long> GameMetadataPlaceIds { get; } = [];

        // Item 2 instrumentation.
        public HashSet<string> ThrowExpiredForCookie { get; } = [];
        public int GetPresenceCalls { get; private set; }
        public List<string> PresenceCookiesQueried { get; } = [];

        // Concurrency-cap test gate: when set, every presence call parks on _gate so the test can
        // measure how many run simultaneously before releasing them.
        public bool GateConcurrentCalls { get; set; }
        public int MaxConcurrentCalls { get; private set; }

        public async Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds)
        {
            lock (_sync)
            {
                GetPresenceCalls++;
                PresenceCookiesQueried.Add(cookie);
                _inFlight++;
                if (_inFlight > MaxConcurrentCalls) MaxConcurrentCalls = _inFlight;
            }

            try
            {
                if (ThrowExpiredForCookie.Contains(cookie))
                {
                    throw new CookieExpiredException();
                }

                if (GateConcurrentCalls)
                {
                    await _gate.Task.ConfigureAwait(false);
                }

                return PresenceByCookie.TryGetValue(cookie, out var p)
                    ? p
                    : throw new InvalidOperationException("FakeRobloxApi: no presence configured for the requested cookie");
            }
            finally
            {
                lock (_sync) _inFlight--;
            }
        }

        /// <summary>Release the concurrency gate so all parked presence calls complete.</summary>
        public void ReleaseGate() => _gate.TrySetResult();

        /// <summary>
        /// Spin until at least <paramref name="expected"/> presence calls are parked on the gate
        /// (or all calls have started). Lets the cap test settle without a fixed sleep.
        /// </summary>
        public async Task WaitForCallsToParkAsync(int expected)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                int inFlight, started;
                lock (_sync)
                {
                    inFlight = _inFlight;
                    started = GetPresenceCalls;
                }
                // Either the cap's worth are parked, or every account has already been dispatched.
                if (inFlight >= expected || started >= PresenceByCookie.Count)
                {
                    return;
                }
                await Task.Delay(5).ConfigureAwait(false);
            }
        }

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
