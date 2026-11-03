using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 4: <see cref="GameIconCache"/> and its lane on <see cref="AccountFactsService"/>'s one
/// worker. Temp files, a scripted metadata API, a fake clock. No network.
/// </summary>
public class GameIconCacheTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rororo-icons-{Guid.NewGuid():N}");
    private string FilePath => Path.Combine(_dir, "game-icons.json");

    public GameIconCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private GameIconCache NewCache(MetadataApi api) => new(api, FilePath, new FakeTimeProvider(T0), NullLogger<GameIconCache>.Instance);

    [Fact]
    public async Task AHit_AnswersFromDisk_WithNoRequestAndNoEnqueue()
    {
        await File.WriteAllTextAsync(FilePath,
            """{"version":1,"games":{"42":{"name":"Cached","universeId":7,"iconUrl":"https://example.invalid/42.png","fetchedAtUtc":"2026-10-30T00:00:00+00:00"}}}""");
        var api = new MetadataApi();
        var cache = NewCache(api);
        var enqueued = new List<long>();
        cache.AttachQueue(id => { enqueued.Add(id); return true; });

        var info = cache.TryGet(42);

        Assert.NotNull(info);
        Assert.Equal("Cached", info!.Name);
        Assert.Equal(7, info.UniverseId);
        Assert.False(cache.Request(42));
        Assert.Empty(enqueued);
        Assert.Equal(0, api.Calls);
    }

    [Fact]
    public async Task AMiss_EnqueuesExactlyOneLookup_AndTheFileGainsTheRowOnIconResolved()
    {
        var api = new MetadataApi();
        api.Rows[42] = new GameMetadata(42, 7, "Fresh", "https://example.invalid/42.png");
        var cache = NewCache(api);
        var enqueued = new List<long>();
        cache.AttachQueue(id => { enqueued.Add(id); return true; });
        GameIconResolvedEventArgs? resolved = null;
        cache.IconResolved += (_, e) => resolved = e;

        Assert.Null(cache.TryGet(42));
        Assert.True(cache.Request(42));
        Assert.False(cache.Request(42)); // outstanding: not queued twice
        Assert.Equal(new long[] { 42 }, enqueued);
        Assert.Equal(0, api.Calls);      // asking is not fetching; the worker fetches

        Assert.True(await cache.ResolveAsync(42));

        Assert.Equal(1, api.Calls);
        Assert.Equal(42, resolved!.PlaceId);
        Assert.Equal("Fresh", resolved.Info.Name);
        var onDisk = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath)).RootElement.GetProperty("games").GetProperty("42");
        Assert.Equal("https://example.invalid/42.png", onDisk.GetProperty("iconUrl").GetString());
        Assert.Equal(7, onDisk.GetProperty("universeId").GetInt64());
        Assert.Equal("Fresh", onDisk.GetProperty("name").GetString());
        Assert.True(onDisk.TryGetProperty("fetchedAtUtc", out _));
        Assert.False(cache.Request(42)); // now a hit
    }

    [Fact]
    public async Task TheFileDeletedAtRuntime_IsRecreated_AndNothingElseIsLost()
    {
        var api = new MetadataApi();
        api.Rows[1] = new GameMetadata(1, 10, "One", "https://example.invalid/1.png");
        api.Rows[2] = new GameMetadata(2, 20, "Two", "https://example.invalid/2.png");
        var cache = NewCache(api);
        await cache.ResolveAsync(1);
        Assert.True(File.Exists(FilePath));

        File.Delete(FilePath);
        await cache.ResolveAsync(2);

        Assert.True(File.Exists(FilePath));
        var games = JsonDocument.Parse(await File.ReadAllTextAsync(FilePath)).RootElement.GetProperty("games");
        Assert.True(games.TryGetProperty("1", out _)); // the row written before the delete survived
        Assert.True(games.TryGetProperty("2", out _));
        Assert.Equal("One", cache.TryGet(1)!.Name);
    }

    [Fact]
    public async Task ACorruptOrEmptyFile_ReadsAsAnEmptyCache_AndNeverThrows()
    {
        await File.WriteAllTextAsync(FilePath, "{ this is not json");
        Assert.Null(NewCache(new MetadataApi()).TryGet(1));

        await File.WriteAllBytesAsync(FilePath, []);
        Assert.Null(NewCache(new MetadataApi()).TryGet(1));
    }

    [Fact]
    public async Task AFailedLookup_WritesNothing_AndIsNotRetriedThisRun()
    {
        var api = new MetadataApi(); // knows no place: returns null
        var cache = NewCache(api);
        var enqueued = new List<long>();
        cache.AttachQueue(id => { enqueued.Add(id); return true; });

        Assert.True(cache.Request(9));
        await cache.ResolveAsync(9);

        Assert.False(File.Exists(FilePath));
        Assert.False(cache.Request(9));
        Assert.Single(enqueued);
    }

    [Fact]
    public void AMissWithNoWorkerAttached_EnqueuesNothing()
    {
        var cache = NewCache(new MetadataApi());
        Assert.False(cache.Request(5));
    }

    // ---- the lane: a second message kind on the facts worker ----

    [Fact]
    public async Task RequestingThroughTheWorker_ResolvesOnce_RaisesIconResolved_AndSharesThePacing()
    {
        var api = new MetadataApi();
        api.Rows[1] = new GameMetadata(1, 10, "One", "https://example.invalid/1.png");
        api.Rows[2] = new GameMetadata(2, 20, "Two", "https://example.invalid/2.png");
        var clock = new FakeTimeProvider(T0);
        var cache = new GameIconCache(api, FilePath, clock);
        using var store = new AccountStore(Path.Combine(_dir, "accounts.dat"));
        using var service = new AccountFactsService(
            store, new NoFacts(), () => [], time: clock, icons: cache);
        var resolved = new List<long>();
        cache.IconResolved += (_, e) => { lock (resolved) { resolved.Add(e.PlaceId); } };

        Assert.True(cache.Request(1));
        Assert.True(cache.Request(2));
        Assert.False(cache.Request(1));
        Assert.False(service.EnqueuePlace(2)); // already queued or in flight: de-duplicated per kind

        await PumpUntilAsync(clock, () => { lock (resolved) { return resolved.Count == 1; } });
        // The second lookup waits the jittered inter-request delay (2.0-3.0 s) after the first ended.
        Assert.Equal(1, api.Calls);
        clock.Advance(TimeSpan.FromMilliseconds(1900));
        await Task.Delay(100);
        Assert.Equal(1, api.Calls);

        await PumpUntilAsync(clock, () => { lock (resolved) { return resolved.Count == 2; } });
        Assert.Equal(new long[] { 1, 2 }, resolved.OrderBy(x => x).ToArray());
        Assert.Equal(2, api.Calls);
        Assert.Equal(2, service.PlaceRequestCount);
        Assert.Equal(0, service.RequestCount); // no account lookup was made
    }

    [Fact]
    public async Task ConstructingTheLaneAndLoadingAnAccountList_FetchesNoIcon()
    {
        var api = new MetadataApi();
        var clock = new FakeTimeProvider(T0);
        var cache = new GameIconCache(api, FilePath, clock);
        using var store = new AccountStore(Path.Combine(_dir, "accounts.dat"));
        using var service = new AccountFactsService(store, new NoFacts(), () => [], time: clock, icons: cache);

        service.Start();
        await Task.Delay(100);

        Assert.Equal(0, api.Calls);
        Assert.Equal(0, service.PlaceRequestCount);
    }

    private static async Task PumpUntilAsync(FakeTimeProvider clock, Func<bool> done)
    {
        for (var i = 0; i < 2000; i++)
        {
            if (done()) { return; }
            clock.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(3);
        }
        throw new TimeoutException("The icon lane did not reach the expected state.");
    }

    private sealed class MetadataApi : IRobloxApi
    {
        private int _calls;
        public Dictionary<long, GameMetadata> Rows { get; } = new();
        public int Calls => Volatile.Read(ref _calls);

        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(Rows.TryGetValue(placeId, out var m) ? m : null);
        }

        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotSupportedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotSupportedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotSupportedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotSupportedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotSupportedException();
    }

    private sealed class NoFacts : IAccountFactsApi
    {
        public Task<long?> GetRobuxAsync(string cookie, CancellationToken ct) => throw new NotSupportedException();
        public Task<int?> GetFriendsOnlineCountAsync(string cookie, long userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<MembershipProbe> GetMembershipAsync(string cookie, long userId, CancellationToken ct) => throw new NotSupportedException();
    }
}
