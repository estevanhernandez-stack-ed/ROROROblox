using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ROROROblox.App.Plugins;
using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

/// <summary>
/// One bad row costs that row, not the file. Item 3 made a wrong-shape store file read as empty, which
/// kept the app starting but meant the next save wrote that empty list over every good row: one bad
/// favorite lost every favorite. Each non-vault store now keeps the good rows, drops the bad ones, warns
/// once with the file name and the dropped count, and the next save writes the good rows back and nothing
/// else. A missing or null container still reads as empty (the table in <c>StoreWrongShapeTests</c>).
/// Temp folders only, fake values only.
/// </summary>
public sealed class StoreSalvageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rororo-salvage-{Guid.NewGuid():N}");

    public StoreSalvageTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    private static byte[] Protect(string json) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(json), optionalEntropy: null, DataProtectionScope.CurrentUser);

    private static string Warning(CapturingLogger<FavoriteGameStore> log) => Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));

    private const string Guid1 = "00000000-0000-0000-0000-000000000001";
    private const string Guid2 = "00000000-0000-0000-0000-000000000002";

    // ---- favorites ----

    private const string GoodFavorite = """{"placeId":10,"universeId":1,"name":"Good","thumbnailUrl":"","isDefault":false,"addedAt":"2026-01-01T00:00:00+00:00"}""";

    [Theory]
    [InlineData("null")]
    [InlineData("""{"placeId":5}""")] // a row with no name
    public async Task Favorites_OneBadRow_KeepsTheGoodRow_AndTheNextSaveWritesOnlyGoodRows(string badRow)
    {
        File.WriteAllText(P("f.json"), $$"""{"version":1,"favorites":[{{GoodFavorite}},{{badRow}}]}""");
        var log = new CapturingLogger<FavoriteGameStore>();
        using var store = new FavoriteGameStore(P("f.json"), log);

        var list = await store.ListAsync();
        Assert.Equal("Good", Assert.Single(list).Name);

        await store.AddAsync(20, 2, "Second", "t");

        using var doc = JsonDocument.Parse(File.ReadAllText(P("f.json")));
        var rows = doc.RootElement.GetProperty("favorites").EnumerateArray().ToList();
        Assert.Equal(["Good", "Second"], rows.Select(r => r.GetProperty("name").GetString()));
        var warning = Warning(log);
        Assert.Contains("f.json", warning);
        Assert.Contains("1 ", warning);
        Assert.DoesNotContain(_dir, warning);
    }

    [Fact]
    public async Task Favorites_SeveralBadRows_WarnOnceWithTheCount()
    {
        File.WriteAllText(P("f.json"), $$"""{"version":1,"favorites":[null,{{GoodFavorite}},{"placeId":5},null]}""");
        var log = new CapturingLogger<FavoriteGameStore>();
        using var store = new FavoriteGameStore(P("f.json"), log);

        await store.ListAsync();
        await store.ListAsync();
        await store.GetDefaultAsync();

        Assert.Contains("3 ", Warning(log));
    }

    [Theory]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"favorites":null}""")]
    [InlineData("null")]
    public async Task Favorites_MissingOrNullContainer_StillReadsAsEmpty(string shape)
    {
        File.WriteAllText(P("f.json"), shape);
        using var store = new FavoriteGameStore(P("f.json"));
        Assert.Empty(await store.ListAsync());
    }

    // ---- private servers ----

    private static string GoodServer(string guid, string code) =>
        $$"""{"id":"{{guid}}","placeId":5,"code":"{{code}}","codeKind":0,"name":"Srv","placeName":"P","thumbnailUrl":"","addedAt":"2026-01-01T00:00:00+00:00"}""";

    [Fact]
    public async Task PrivateServers_NullRowAndUnreconstructableRow_AreDropped_AndTheGoodRowsSurviveASave()
    {
        File.WriteAllText(P("ps.json"),
            $$"""{"version":1,"servers":[{{GoodServer(Guid1, "code-a")}},null,{"placeId":5},{{GoodServer(Guid2, "code-b")}}]}""");
        var log = new CapturingLogger<PrivateServerStore>();
        using var store = new PrivateServerStore(P("ps.json"), log);

        Assert.Equal(["code-a", "code-b"], (await store.ListAsync()).Select(s => s.Code));

        await store.AddAsync(7, "code-c", PrivateServerCodeKind.LinkCode, "n", "p", "t");

        using var doc = JsonDocument.Parse(File.ReadAllText(P("ps.json")));
        Assert.Equal(3, doc.RootElement.GetProperty("servers").GetArrayLength());
        var warning = Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("ps.json", warning);
        Assert.Contains("2 ", warning);
    }

    // ---- session history ----

    private static string Session(string id, string name) =>
        $$"""{"id":"{{id}}","accountId":"{{Guid1}}","accountDisplayName":"{{name}}","isPrivateServer":false,"launchedAtUtc":"2026-01-01T00:00:00+00:00"}""";

    [Fact]
    public async Task SessionHistory_OneBadSession_KeepsTheRest_AndASaveWritesOnlyGoodRows()
    {
        File.WriteAllText(P("h.json"),
            $$"""{"version":1,"sessions":[{{Session(Guid1, "One")}},null,{"id":"{{Guid2}}"},{{Session(Guid2, "Two")}}]}""");
        var log = new CapturingLogger<SessionHistoryStore>();
        using var store = new SessionHistoryStore(P("h.json"), log);

        var sessions = await store.ListAsync();
        Assert.Equal(2, sessions.Count);

        await store.AddAsync(new LaunchSession(Guid.NewGuid(), Guid.NewGuid(), "Three", null, null, null, false, DateTimeOffset.UtcNow, null, null));

        using var doc = JsonDocument.Parse(File.ReadAllText(P("h.json")));
        var rows = doc.RootElement.GetProperty("sessions").EnumerateArray().ToList();
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(JsonValueKind.Object, r.ValueKind));
        var warning = Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("h.json", warning);
        Assert.Contains("2 ", warning);
    }

    // ---- session stats: salvage at each level ----

    [Fact]
    public async Task SessionStats_ABadAccountAndABadGame_AreDropped_AndEveryOtherLevelSurvives()
    {
        File.WriteAllText(P("st.json"), $$$"""
            {"accounts":{"{{{Guid1}}}":{"launches":4},"{{{Guid2}}}":null},
             "games":{"1":{"launches":2},"2":null},
             "days":{"2026-01-01":{"launches":1}},
             "months":{"2025-12":{"launches":9,"daysPlayed":3}},
             "peakConcurrentAlts":6}
            """);
        var log = new CapturingLogger<SessionStatsStore>();
        using var store = new SessionStatsStore(P("st.json"), log);

        var stats = await store.ReadAsync();

        Assert.Equal(4, Assert.Single(stats.Accounts).Value.Launches);
        Assert.Equal(2, Assert.Single(stats.Games).Value.Launches);
        Assert.Single(stats.Days);
        Assert.Single(stats.Months);
        Assert.Equal(6, stats.PeakConcurrentAlts);

        await store.ApplyAsync(new StatsEvent.ConcurrencyObserved(7));

        using var doc = JsonDocument.Parse(File.ReadAllText(P("st.json")));
        Assert.Equal(1, doc.RootElement.GetProperty("accounts").EnumerateObject().Count());
        Assert.Equal(1, doc.RootElement.GetProperty("games").EnumerateObject().Count());
        Assert.Equal(7, doc.RootElement.GetProperty("peakConcurrentAlts").GetInt32());
        var warning = Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("st.json", warning);
        Assert.Contains("2 ", warning);
    }

    [Fact]
    public async Task SessionStats_ANullSection_IsEmpty_AndTheOtherSectionsAreKept()
    {
        File.WriteAllText(P("st.json"), """{"accounts":null,"months":{"2025-12":{"launches":9,"daysPlayed":3}}}""");
        using var store = new SessionStatsStore(P("st.json"));

        var stats = await store.ReadAsync();

        Assert.Empty(stats.Accounts);
        Assert.Equal(9, Assert.Single(stats.Months).Value.Launches);
    }

    // ---- streamer identities ----

    [Fact]
    public async Task Streamer_ABadEntry_IsDropped_AndTheGoodEntriesSurviveASave()
    {
        File.WriteAllText(P("s.dat"), """{"a":{"FakeName":"Alpha","FakeAvatarId":"1"},"b":null,"c":{},"d":{"FakeName":"Delta","FakeAvatarId":"4"}}""");
        var log = new CapturingLogger<FileStreamerIdentityStore>();
        var store = new FileStreamerIdentityStore(P("s.dat"), log);

        var all = await store.LoadAllAsync();
        Assert.Equal(["a", "d"], all.Keys.OrderBy(k => k));

        await store.SaveAsync("e", new StreamerIdentity("Echo", "5"));

        using var doc = JsonDocument.Parse(File.ReadAllText(P("s.dat")));
        Assert.Equal(["a", "d", "e"], doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        var warning = Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("s.dat", warning);
        Assert.Contains("2 ", warning);
    }

    // ---- game icon cache ----

    [Fact]
    public void GameIcons_ANullRow_CostsThatIcon_AndTheRestOfTheCacheIsKept()
    {
        File.WriteAllText(P("icons.json"), """{"version":1,"games":{"1":{"name":"One","universeId":9,"iconUrl":"u","fetchedAtUtc":"2026-01-01T00:00:00+00:00"},"2":null}}""");
        var log = new CapturingLogger<GameIconCache>();
        var cache = new GameIconCache(new NoApi(), P("icons.json"), log: log);

        Assert.Equal("One", cache.TryGet(1)?.Name);
        Assert.Null(cache.TryGet(2));
        var warning = Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("icons.json", warning);
        Assert.Contains("1 ", warning);
    }

    [Fact]
    public async Task GameIcons_AResolveAfterASalvagedLoad_WritesTheGoodRowsBack()
    {
        File.WriteAllText(P("icons.json"), """{"version":1,"games":{"1":{"name":"One","universeId":9,"iconUrl":"u","fetchedAtUtc":"2026-01-01T00:00:00+00:00"},"2":null}}""");
        var cache = new GameIconCache(new ResolvingApi(), P("icons.json"));
        Assert.NotNull(cache.TryGet(1));

        await cache.ResolveAsync(3);

        using var doc = JsonDocument.Parse(File.ReadAllText(P("icons.json")));
        Assert.Equal(["1", "3"], doc.RootElement.GetProperty("games").EnumerateObject().Select(p => p.Name).OrderBy(n => n));
    }

    // ---- consent ----

    [Fact]
    public async Task Consent_OneBadRecord_CostsThatPluginsConsent_AndTheOthersSurvive()
    {
        // A dropped record means that plugin asks for consent again, the safe direction.
        File.WriteAllBytes(P("consent.dat"), Protect("""
            [{"PluginId":"keep.me","GrantedCapabilities":["a"]},
             null,
             {"PluginId":null,"GrantedCapabilities":[]},
             {"PluginId":"no.caps","GrantedCapabilities":null},
             {"PluginId":"twice","GrantedCapabilities":["a"]},{"PluginId":"twice","GrantedCapabilities":["b"]},
             {"PluginId":"keep.too","GrantedCapabilities":[]}]
            """));
        var log = new CapturingLogger<ConsentStore>();
        var store = new ConsentStore(P("consent.dat"), log);

        var records = await store.ListAsync();
        Assert.Equal(["keep.me", "keep.too"], records.Select(r => r.PluginId).OrderBy(i => i));

        await store.GrantAsync("new.one", ["c"]);

        var after = await store.ListAsync();
        Assert.Equal(["keep.me", "keep.too", "new.one"], after.Select(r => r.PluginId).OrderBy(i => i));
        var warning = Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("consent.dat", warning);
        Assert.Contains("5 ", warning); // null, no id, no capability list, and both rows of the repeated id
    }

    private sealed class NoApi : IRobloxApi
    {
        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId) => throw new NotSupportedException();
        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotSupportedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotSupportedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotSupportedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotSupportedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotSupportedException();
    }

    private sealed class ResolvingApi : IRobloxApi
    {
        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId) =>
            Task.FromResult<GameMetadata?>(new GameMetadata(placeId, 77, "Three", "https://example.invalid/icon.png"));
        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotSupportedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotSupportedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotSupportedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotSupportedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotSupportedException();
    }
}
