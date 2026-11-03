using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 4: the Library join. Real stores over temp files (fake codes only), a counting icon
/// cache so "the join never fetches" is an assertion rather than a hope.
/// </summary>
public class LibraryCatalogTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Alt = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rororo-library-{Guid.NewGuid():N}");
    private readonly FavoriteGameStore _favorites;
    private readonly PrivateServerStore _servers;
    private readonly SessionStatsStore _stats;
    private readonly SessionHistoryStore _history;
    private readonly CountingIcons _icons = new();

    public LibraryCatalogTests()
    {
        Directory.CreateDirectory(_dir);
        _favorites = new FavoriteGameStore(Path.Combine(_dir, "favorites.json"));
        _servers = new PrivateServerStore(Path.Combine(_dir, "private-servers.json"));
        _stats = new SessionStatsStore(Path.Combine(_dir, "session-stats.json"));
        _history = new SessionHistoryStore(Path.Combine(_dir, "session-history.json"));
    }

    public void Dispose()
    {
        _stats.Dispose();
        _history.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private LibraryCatalog Catalog() => new(_favorites, _servers, _stats, _history, _icons);

    private Task Played(long place, string name, DateTimeOffset at, int minutes = 0) => PlayedAsync(place, name, at, minutes);

    private async Task PlayedAsync(long place, string name, DateTimeOffset at, int minutes)
    {
        await _stats.ApplyAsync(new StatsEvent.LaunchRecorded(Alt, place, name, at));
        if (minutes > 0)
        {
            await _stats.ApplyAsync(new StatsEvent.SessionEnded(Alt, place, at, at.AddMinutes(minutes)));
        }
    }

    [Fact]
    public async Task EmptyStores_YieldAnEmptyList()
    {
        Assert.Empty(await Catalog().ListAsync());
    }

    [Fact]
    public async Task AFavourite_ASavedServer_AndAnUnsavedPlayedPlace_ComeBackAsThreeKinds_InThatOrder()
    {
        await _favorites.AddAsync(100, 1000, "Fav Game", "https://example.invalid/fav.png");
        await _servers.AddAsync(200, "FAKE-LINK-CODE", PrivateServerCodeKind.LinkCode, "Clan Server", "Server Place", "https://example.invalid/ps.png");
        await Played(300, "Played Game", T0);

        var list = await Catalog().ListAsync();

        Assert.Equal(
            new[] { LibraryEntryKind.Favourite, LibraryEntryKind.PrivateServer, LibraryEntryKind.Recent },
            list.Select(e => e.Kind).ToArray());
        Assert.Equal(new LaunchTarget.Place(100), list[0].Target);
        var server = list[1];
        Assert.Equal(new LaunchTarget.PrivateServer(200, "FAKE-LINK-CODE", PrivateServerCodeKind.LinkCode), server.Target);
        Assert.NotNull(server.PrivateServerId);
        Assert.Equal(new LaunchTarget.Place(300), list[2].Target);
        Assert.Equal("Played Game", list[2].Name);
        Assert.False(list[2].IsDefault);
    }

    [Fact]
    public async Task APlaceThatIsFavouriteAndPlayed_ComesBackOnce_AsAFavourite_WithTheStatsLastPlayed()
    {
        await _favorites.AddAsync(100, 1000, "Fav Game", "https://example.invalid/fav.png");
        await Played(100, "Fav Game", T0, minutes: 45);
        await Played(100, "Fav Game", T0.AddDays(2), minutes: 15);

        var list = await Catalog().ListAsync();

        var only = Assert.Single(list);
        Assert.Equal(LibraryEntryKind.Favourite, only.Kind);
        Assert.Equal(T0.AddDays(2), only.LastPlayedUtc);
        Assert.Equal(TimeSpan.FromMinutes(60), only.TotalPlayed);
    }

    [Fact]
    public async Task WithinAKind_OrderIsLastPlayedDescending_NeverPlayedLast()
    {
        await _favorites.AddAsync(1, 10, "Zulu", "t");
        await _favorites.AddAsync(2, 20, "Alpha", "t");
        await _favorites.AddAsync(3, 30, "Mike", "t");
        await Played(1, "Zulu", T0.AddHours(5));
        await Played(3, "Mike", T0.AddHours(1));
        await Played(7, "Old", T0.AddDays(-9));
        await Played(8, "New", T0.AddDays(-1));

        var list = await Catalog().ListAsync();

        Assert.Equal(new long[] { 1, 3, 2 }, list.Where(e => e.Kind == LibraryEntryKind.Favourite).Select(e => e.PlaceId).ToArray());
        Assert.Equal(new long[] { 8, 7 }, list.Where(e => e.Kind == LibraryEntryKind.Recent).Select(e => e.PlaceId).ToArray());
    }

    [Fact]
    public async Task ASavedServers_LastPlayed_AndTotal_ComeFromTheHistoryRowsThatNameIt()
    {
        var server = await _servers.AddAsync(200, "FAKE-LINK-CODE", PrivateServerCodeKind.LinkCode, "Clan Server", "Place", "t");
        await _history.AddAsync(Row(server.Id, T0, TimeSpan.FromMinutes(30)));
        await _history.AddAsync(Row(server.Id, T0.AddHours(3), TimeSpan.FromMinutes(20)));
        await _history.AddAsync(Row(Guid.NewGuid(), T0.AddHours(9), TimeSpan.FromMinutes(99))); // another server

        var entry = Assert.Single(await Catalog().ListAsync());

        Assert.Equal(T0.AddHours(3), entry.LastPlayedUtc);
        Assert.Equal(TimeSpan.FromMinutes(50), entry.TotalPlayed);
    }

    [Fact]
    public async Task ListAsync_OverManyUnsavedPlaces_EnqueuesZeroLookups_AndReadsIconsFromTheCacheStateOnly()
    {
        for (var i = 1; i <= 25; i++) { await Played(5000 + i, $"Game {i}", T0.AddMinutes(i)); }
        _icons.Known[5003] = new GameIconInfo("Cached Name", 9, "https://example.invalid/3.png", T0);

        var list = await Catalog().ListAsync();

        Assert.Equal(25, list.Count);
        Assert.Equal(0, _icons.RequestCalls);
        Assert.Equal(0, _icons.ResolveCalls);
        Assert.Equal("https://example.invalid/3.png", list.Single(e => e.PlaceId == 5003).IconUrl);
        Assert.Equal(24, list.Count(e => e.IconUrl is null)); // a miss is null, not a fetch
    }

    private static LaunchSession Row(Guid serverId, DateTimeOffset at, TimeSpan length) => new(
        Guid.NewGuid(), Alt, "Alt", null, "Clan Server", 200, true, at, at + length, null, PrivateServerId: serverId);

    private sealed class CountingIcons : IGameIconCache
    {
        public readonly Dictionary<long, GameIconInfo> Known = new();
        public int RequestCalls;
        public int ResolveCalls;

        public event EventHandler<GameIconResolvedEventArgs>? IconResolved { add { } remove { } }
        public GameIconInfo? TryGet(long placeId) => Known.TryGetValue(placeId, out var i) ? i : null;
        public bool Request(long placeId) { RequestCalls++; return true; }
        public Task<bool> ResolveAsync(long placeId, CancellationToken ct = default) { ResolveCalls++; return Task.FromResult(true); }
        public void AttachQueue(Func<long, bool> enqueue) { }
    }
}
