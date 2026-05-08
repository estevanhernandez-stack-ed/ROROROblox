using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Unit tests for <see cref="JoinByLinkSave.ApplyAsync"/> — the save-on-paste branch
/// behind the "Save to my library" checkbox on JoinByLinkWindow. 7 cases per spec §8.1.
/// All fakes hand-rolled (zero new dependencies — spec §3).
/// </summary>
public class JoinByLinkSaveTests
{
    private const long TestPlaceId = 920587237L;
    private const long TestUniverseId = 12345L;
    private const string TestPlaceName = "Pet Simulator 99";
    private const string TestThumbnail = "https://thumb.example/pet-sim-99.png";
    private const string TestPrivateCode = "private-share-abc";

    private static GameMetadata DefaultMetadata() =>
        new(TestPlaceId, TestUniverseId, TestPlaceName, TestThumbnail);

    [Fact]
    public async Task ApplyAsync_PlaceWithSaveTrue_CallsFavoriteAddAsyncOnce()
    {
        var api = new FakeRobloxApi { GameMetadataResult = DefaultMetadata() };
        var favorites = new FakeFavoriteGameStore();
        var servers = new FakePrivateServerStore();
        var target = new LaunchTarget.Place(TestPlaceId);

        await JoinByLinkSave.ApplyAsync(api, favorites, servers, target, saveToLibrary: true);

        Assert.Single(favorites.AddCalls);
        Assert.Equal(TestPlaceId, favorites.AddCalls[0].PlaceId);
        Assert.Equal(TestUniverseId, favorites.AddCalls[0].UniverseId);
        Assert.Equal(TestPlaceName, favorites.AddCalls[0].Name);
        Assert.Equal(TestThumbnail, favorites.AddCalls[0].ThumbnailUrl);
        Assert.Empty(servers.AddCalls);
    }

    [Fact]
    public async Task ApplyAsync_PrivateServerWithSaveTrue_CallsPrivateServerAddAsyncOnce()
    {
        var api = new FakeRobloxApi { GameMetadataResult = DefaultMetadata() };
        var favorites = new FakeFavoriteGameStore();
        var servers = new FakePrivateServerStore();
        var target = new LaunchTarget.PrivateServer(TestPlaceId, TestPrivateCode, PrivateServerCodeKind.LinkCode);

        await JoinByLinkSave.ApplyAsync(api, favorites, servers, target, saveToLibrary: true);

        Assert.Single(servers.AddCalls);
        Assert.Equal(TestPlaceId, servers.AddCalls[0].PlaceId);
        Assert.Equal(TestPrivateCode, servers.AddCalls[0].Code);
        Assert.Equal(PrivateServerCodeKind.LinkCode, servers.AddCalls[0].CodeKind);
        Assert.Equal(TestPlaceName, servers.AddCalls[0].Name);
        Assert.Equal(TestPlaceName, servers.AddCalls[0].PlaceName);
        Assert.Equal(TestThumbnail, servers.AddCalls[0].ThumbnailUrl);
        Assert.Empty(favorites.AddCalls);
    }

    [Fact]
    public async Task ApplyAsync_PlaceWithSaveFalse_NoStoreCalled()
    {
        var api = new FakeRobloxApi { GameMetadataResult = DefaultMetadata() };
        var favorites = new FakeFavoriteGameStore();
        var servers = new FakePrivateServerStore();
        var target = new LaunchTarget.Place(TestPlaceId);

        await JoinByLinkSave.ApplyAsync(api, favorites, servers, target, saveToLibrary: false);

        Assert.Empty(favorites.AddCalls);
        Assert.Empty(servers.AddCalls);
        Assert.Equal(0, api.GameMetadataCallCount);
    }

    [Fact]
    public async Task ApplyAsync_PrivateServerWithSaveFalse_NoStoreCalled()
    {
        var api = new FakeRobloxApi { GameMetadataResult = DefaultMetadata() };
        var favorites = new FakeFavoriteGameStore();
        var servers = new FakePrivateServerStore();
        var target = new LaunchTarget.PrivateServer(TestPlaceId, TestPrivateCode, PrivateServerCodeKind.LinkCode);

        await JoinByLinkSave.ApplyAsync(api, favorites, servers, target, saveToLibrary: false);

        Assert.Empty(favorites.AddCalls);
        Assert.Empty(servers.AddCalls);
        Assert.Equal(0, api.GameMetadataCallCount);
    }

    [Fact]
    public async Task ApplyAsync_FavoriteAddThrows_DoesNotPropagate()
    {
        var api = new FakeRobloxApi { GameMetadataResult = DefaultMetadata() };
        var favorites = new FakeFavoriteGameStore { ThrowOnAdd = new InvalidOperationException("disk full") };
        var servers = new FakePrivateServerStore();
        var target = new LaunchTarget.Place(TestPlaceId);

        // Must NOT throw — soft-fail discipline (spec §7).
        await JoinByLinkSave.ApplyAsync(api, favorites, servers, target, saveToLibrary: true);

        Assert.Empty(favorites.AddCalls); // throw happened before the call list was appended
        Assert.Empty(servers.AddCalls);
    }

    [Fact]
    public async Task ApplyAsync_PrivateServerAddThrows_DoesNotPropagate()
    {
        var api = new FakeRobloxApi { GameMetadataResult = DefaultMetadata() };
        var favorites = new FakeFavoriteGameStore();
        var servers = new FakePrivateServerStore { ThrowOnAdd = new InvalidOperationException("permission denied") };
        var target = new LaunchTarget.PrivateServer(TestPlaceId, TestPrivateCode, PrivateServerCodeKind.LinkCode);

        await JoinByLinkSave.ApplyAsync(api, favorites, servers, target, saveToLibrary: true);

        Assert.Empty(favorites.AddCalls);
        Assert.Empty(servers.AddCalls);
    }

    [Fact]
    public async Task ApplyAsync_MetadataReturnsNull_SaveSkippedNoThrow()
    {
        var api = new FakeRobloxApi { GameMetadataResult = null };
        var favorites = new FakeFavoriteGameStore();
        var servers = new FakePrivateServerStore();
        var target = new LaunchTarget.Place(TestPlaceId);

        await JoinByLinkSave.ApplyAsync(api, favorites, servers, target, saveToLibrary: true);

        Assert.Equal(1, api.GameMetadataCallCount);
        Assert.Empty(favorites.AddCalls);
        Assert.Empty(servers.AddCalls);
    }

    [Fact]
    public async Task ApplyAsync_MetadataThrows_SaveSkippedNoThrow()
    {
        var api = new FakeRobloxApi { ThrowOnGameMetadata = new HttpRequestException("network down") };
        var favorites = new FakeFavoriteGameStore();
        var servers = new FakePrivateServerStore();
        var target = new LaunchTarget.PrivateServer(TestPlaceId, TestPrivateCode, PrivateServerCodeKind.LinkCode);

        // Must NOT throw — even unexpected exceptions from a contract-null-returning API
        // get caught (defensive).
        await JoinByLinkSave.ApplyAsync(api, favorites, servers, target, saveToLibrary: true);

        Assert.Empty(favorites.AddCalls);
        Assert.Empty(servers.AddCalls);
    }

    // ---- fakes ----

    private sealed class FakeFavoriteGameStore : IFavoriteGameStore
    {
        public record AddCall(long PlaceId, long UniverseId, string Name, string ThumbnailUrl);

        public List<AddCall> AddCalls { get; } = new();
        public Exception? ThrowOnAdd { get; set; }

        public Task<FavoriteGame> AddAsync(long placeId, long universeId, string name, string thumbnailUrl)
        {
            if (ThrowOnAdd is not null) throw ThrowOnAdd;
            AddCalls.Add(new AddCall(placeId, universeId, name, thumbnailUrl));
            return Task.FromResult(new FavoriteGame(placeId, universeId, name, thumbnailUrl, IsDefault: false, AddedAt: DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<FavoriteGame>> ListAsync() => throw new NotImplementedException();
        public Task<FavoriteGame?> GetDefaultAsync() => throw new NotImplementedException();
        public Task RemoveAsync(long placeId) => throw new NotImplementedException();
        public Task SetDefaultAsync(long placeId) => throw new NotImplementedException();
        public Task UpdateLocalNameAsync(long placeId, string? localName) => throw new NotImplementedException();
        public event EventHandler? DefaultChanged { add { } remove { } }
    }

    private sealed class FakePrivateServerStore : IPrivateServerStore
    {
        public record AddCall(long PlaceId, string Code, PrivateServerCodeKind CodeKind, string Name, string PlaceName, string ThumbnailUrl);

        public List<AddCall> AddCalls { get; } = new();
        public Exception? ThrowOnAdd { get; set; }

        public Task<SavedPrivateServer> AddAsync(long placeId, string code, PrivateServerCodeKind codeKind, string name, string placeName, string thumbnailUrl)
        {
            if (ThrowOnAdd is not null) throw ThrowOnAdd;
            AddCalls.Add(new AddCall(placeId, code, codeKind, name, placeName, thumbnailUrl));
            return Task.FromResult(new SavedPrivateServer(Guid.NewGuid(), placeId, code, codeKind, name, placeName, thumbnailUrl, DateTimeOffset.UtcNow, null));
        }

        public Task<IReadOnlyList<SavedPrivateServer>> ListAsync() => throw new NotImplementedException();
        public Task<SavedPrivateServer?> GetAsync(Guid id) => throw new NotImplementedException();
        public Task RemoveAsync(Guid id) => throw new NotImplementedException();
        public Task TouchLastLaunchedAsync(Guid id) => throw new NotImplementedException();
        public Task UpdateLocalNameAsync(Guid serverId, string? localName) => throw new NotImplementedException();
    }

    private sealed class FakeRobloxApi : IRobloxApi
    {
        public GameMetadata? GameMetadataResult { get; set; }
        public Exception? ThrowOnGameMetadata { get; set; }
        public int GameMetadataCallCount { get; private set; }

        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId)
        {
            GameMetadataCallCount++;
            if (ThrowOnGameMetadata is not null) throw ThrowOnGameMetadata;
            return Task.FromResult(GameMetadataResult);
        }

        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotImplementedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotImplementedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotImplementedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotImplementedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotImplementedException();
    }
}
