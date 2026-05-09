using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Cycle-5 tests for <see cref="AccountUserIdBackfillService"/>. Hand-rolled fakes for
/// <see cref="IAccountStore"/> and <see cref="IRobloxApi"/> (zero new dependencies — same
/// pattern as <see cref="JoinByLinkSaveTests"/>). Spec §4.4, §7. 6 cases:
/// 1. all already backfilled → no-op
/// 2. all missing → resolve + persist each in order
/// 3. mixed → only resolve the missing ones
/// 4. GetUserProfileAsync throws on one → continue, no exception bubbles
/// 5. UpdateRobloxUserIdAsync throws on one → continue, no exception bubbles
/// 6. CancellationToken honored → loop stops cleanly mid-pass
///
/// Tests pass <c>interAccountDelayMs: 0</c> to skip the production stagger so the suite
/// runs in milliseconds.
/// </summary>
public class AccountUserIdBackfillServiceTests
{
    private static Account StubAccount(Guid id, long? userId = null) =>
        new(id, "Test", "https://avatar", DateTimeOffset.UtcNow, null, RobloxUserId: userId);

    [Fact]
    public async Task RunAsync_NoOp_WhenAllAccountsAlreadyBackfilled()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var store = new FakeAccountStore
        {
            Accounts = [StubAccount(id1, 100), StubAccount(id2, 200)],
        };
        var api = new FakeRobloxApi();
        var service = new AccountUserIdBackfillService(store, api, NullLogger<AccountUserIdBackfillService>.Instance, interAccountDelayMs: 0);

        await service.RunAsync();

        Assert.Empty(api.GetUserProfileCalls);
        Assert.Empty(store.UpdateRobloxUserIdCalls);
    }

    [Fact]
    public async Task RunAsync_ResolvesAndPersistsEach_WhenAllAccountsMissingUserId()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var store = new FakeAccountStore
        {
            Accounts = [StubAccount(id1), StubAccount(id2)],
        };
        store.CookieByAccount[id1] = "cookie-1";
        store.CookieByAccount[id2] = "cookie-2";
        var api = new FakeRobloxApi
        {
            ProfileByCookie =
            {
                ["cookie-1"] = new UserProfile(101, "user1", "User One"),
                ["cookie-2"] = new UserProfile(202, "user2", "User Two"),
            },
        };
        var service = new AccountUserIdBackfillService(store, api, NullLogger<AccountUserIdBackfillService>.Instance, interAccountDelayMs: 0);

        await service.RunAsync();

        Assert.Equal(2, api.GetUserProfileCalls.Count);
        Assert.Equal(2, store.UpdateRobloxUserIdCalls.Count);
        Assert.Equal((id1, 101L), store.UpdateRobloxUserIdCalls[0]);
        Assert.Equal((id2, 202L), store.UpdateRobloxUserIdCalls[1]);
    }

    [Fact]
    public async Task RunAsync_OnlyResolvesMissing_WhenMixed()
    {
        var idHas = Guid.NewGuid();
        var idMissing = Guid.NewGuid();
        var store = new FakeAccountStore
        {
            Accounts = [StubAccount(idHas, 999), StubAccount(idMissing)],
        };
        store.CookieByAccount[idMissing] = "cookie-missing";
        var api = new FakeRobloxApi
        {
            ProfileByCookie = { ["cookie-missing"] = new UserProfile(303, "missing-user", "Missing User") },
        };
        var service = new AccountUserIdBackfillService(store, api, NullLogger<AccountUserIdBackfillService>.Instance, interAccountDelayMs: 0);

        await service.RunAsync();

        Assert.Single(api.GetUserProfileCalls);
        Assert.Equal("cookie-missing", api.GetUserProfileCalls[0]);
        var persistCall = Assert.Single(store.UpdateRobloxUserIdCalls);
        Assert.Equal((idMissing, 303L), persistCall);
    }

    [Fact]
    public async Task RunAsync_ContinuesPastFailure_WhenGetUserProfileThrowsOnOneAccount()
    {
        var idGood = Guid.NewGuid();
        var idBad = Guid.NewGuid();
        var store = new FakeAccountStore
        {
            Accounts = [StubAccount(idBad), StubAccount(idGood)],
        };
        store.CookieByAccount[idBad] = "cookie-bad";
        store.CookieByAccount[idGood] = "cookie-good";
        var api = new FakeRobloxApi
        {
            ProfileByCookie = { ["cookie-good"] = new UserProfile(444, "good-user", "Good User") },
            ThrowOnCookie = "cookie-bad",
            ThrowToRaise = new HttpRequestException("network down"),
        };
        var service = new AccountUserIdBackfillService(store, api, NullLogger<AccountUserIdBackfillService>.Instance, interAccountDelayMs: 0);

        // MUST NOT throw — soft-fail at the orchestrator level.
        await service.RunAsync();

        // The "bad" account is skipped, the "good" one persists.
        var persist = Assert.Single(store.UpdateRobloxUserIdCalls);
        Assert.Equal((idGood, 444L), persist);
    }

    [Fact]
    public async Task RunAsync_ContinuesPastFailure_WhenUpdateRobloxUserIdThrowsOnOneAccount()
    {
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        var store = new FakeAccountStore
        {
            Accounts = [StubAccount(idA), StubAccount(idB)],
            ThrowOnUpdateForAccount = idA, // first one fails to persist
            ThrowToRaise = new IOException("disk full"),
        };
        store.CookieByAccount[idA] = "cookie-a";
        store.CookieByAccount[idB] = "cookie-b";
        var api = new FakeRobloxApi
        {
            ProfileByCookie =
            {
                ["cookie-a"] = new UserProfile(111, "a", "A"),
                ["cookie-b"] = new UserProfile(222, "b", "B"),
            },
        };
        var service = new AccountUserIdBackfillService(store, api, NullLogger<AccountUserIdBackfillService>.Instance, interAccountDelayMs: 0);

        await service.RunAsync();

        // Both accounts attempted. First persist throws (captured but not in calls list since
        // the throw happens before the capture). Second account persists successfully.
        Assert.Equal(2, api.GetUserProfileCalls.Count); // both queried
        var persist = Assert.Single(store.UpdateRobloxUserIdCalls); // only the one that succeeded
        Assert.Equal((idB, 222L), persist);
    }

    [Fact]
    public async Task RunAsync_RespectsCancellationToken_StopsMidPass()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();
        var store = new FakeAccountStore
        {
            Accounts = [StubAccount(id1), StubAccount(id2), StubAccount(id3)],
        };
        store.CookieByAccount[id1] = "cookie-1";
        store.CookieByAccount[id2] = "cookie-2";
        store.CookieByAccount[id3] = "cookie-3";
        var api = new FakeRobloxApi
        {
            ProfileByCookie =
            {
                ["cookie-1"] = new UserProfile(1, "one", "One"),
                ["cookie-2"] = new UserProfile(2, "two", "Two"),
                ["cookie-3"] = new UserProfile(3, "three", "Three"),
            },
        };
        var cts = new CancellationTokenSource();
        // Cancel after the first persist so the loop exits before processing id2/id3.
        store.OnUpdate = (acctId, _) =>
        {
            if (acctId == id1) cts.Cancel();
        };
        var service = new AccountUserIdBackfillService(store, api, NullLogger<AccountUserIdBackfillService>.Instance, interAccountDelayMs: 0);

        try { await service.RunAsync(cts.Token); }
        catch (OperationCanceledException) { /* expected — cancellation surfaces cleanly */ }

        // First account persisted; second + third skipped because of cancel.
        var persist = Assert.Single(store.UpdateRobloxUserIdCalls);
        Assert.Equal((id1, 1L), persist);
    }

    // ---- fakes ----

    private sealed class FakeAccountStore : IAccountStore
    {
        public List<Account> Accounts { get; init; } = [];
        public Dictionary<Guid, string> CookieByAccount { get; } = [];
        public List<(Guid AccountId, long UserId)> UpdateRobloxUserIdCalls { get; } = [];
        public Guid? ThrowOnUpdateForAccount { get; set; }
        public Exception? ThrowToRaise { get; set; }
        public Action<Guid, long>? OnUpdate { get; set; }

        public Task<IReadOnlyList<Account>> ListAsync() => Task.FromResult<IReadOnlyList<Account>>(Accounts);

        public Task<string> RetrieveCookieAsync(Guid id) =>
            CookieByAccount.TryGetValue(id, out var c) ? Task.FromResult(c) : Task.FromResult(string.Empty);

        public Task UpdateRobloxUserIdAsync(Guid accountId, long userId)
        {
            OnUpdate?.Invoke(accountId, userId);
            if (ThrowOnUpdateForAccount == accountId && ThrowToRaise is not null) throw ThrowToRaise;
            UpdateRobloxUserIdCalls.Add((accountId, userId));
            return Task.CompletedTask;
        }

        // Unused by AccountUserIdBackfillService — throw to surface accidental use.
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
    }

    private sealed class FakeRobloxApi : IRobloxApi
    {
        public Dictionary<string, UserProfile> ProfileByCookie { get; } = [];
        public List<string> GetUserProfileCalls { get; } = [];
        public string? ThrowOnCookie { get; set; }
        public Exception? ThrowToRaise { get; set; }

        public Task<UserProfile> GetUserProfileAsync(string cookie)
        {
            GetUserProfileCalls.Add(cookie);
            if (ThrowOnCookie == cookie && ThrowToRaise is not null) throw ThrowToRaise;
            return ProfileByCookie.TryGetValue(cookie, out var p)
                ? Task.FromResult(p)
                : throw new InvalidOperationException($"FakeRobloxApi: no UserProfile configured for cookie '{cookie}'");
        }

        // Unused — throw to surface accidental use.
        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotImplementedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotImplementedException();
        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId) => throw new NotImplementedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotImplementedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotImplementedException();
    }
}
