using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 5: the per-account panel, with no WPF. Every dependency is a hand-rolled fake; the
/// history is the only input, so the isolation test is a statement about the query, not about a store.
/// </summary>
public class AccountPanelViewModelTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AccountSummary Owner(Guid? id = null, AccountFacts? facts = null, bool? persisted = null) =>
        new(new Account(id ?? Guid.NewGuid(), "Alt", "", Now, null, Facts: facts, PanelExpanded: persisted));

    private static LaunchSession Row(Guid account, long place, string name, DateTimeOffset at, int minutes = 10, Guid? server = null, bool isPrivate = false) =>
        new(Guid.NewGuid(), account, "Alt", null, name, place, isPrivate || server is not null, at, at.AddMinutes(minutes), null, PrivateServerId: server);

    private sealed class Harness
    {
        public List<LaunchSession> History = new();
        public List<LibraryEntry> Library = new();
        public List<LaunchTarget> Launched = new();
        public int FactsRequests;
        public FakeIcons Icons = new();
        public FakeFavorites Favorites = new();

        public AccountPanelViewModel Panel(AccountSummary owner, bool withIcons = true) => new(
            owner,
            () => Task.FromResult<IReadOnlyList<LaunchSession>>(History.ToList()),
            () => Task.FromResult<IReadOnlyList<LibraryEntry>>(Library.ToList()),
            t => { Launched.Add(t); return Task.CompletedTask; },
            () => { FactsRequests++; return Task.CompletedTask; },
            withIcons ? Icons : null,
            Favorites,
            new InlineUi());
    }

    private static LibraryEntry Fav(long place, string name, DateTimeOffset? last = null) =>
        new(LibraryEntryKind.Favourite, place, null, name, null, "https://example.invalid/f.png", false, last, null, new LaunchTarget.Place(place));

    private static LibraryEntry Server(Guid id, long place, string name) =>
        new(LibraryEntryKind.PrivateServer, place, id, name, null, null, false, null, null, new LaunchTarget.PrivateServer(place, "FAKE-CODE", PrivateServerCodeKind.AccessCode));

    // ------------------------------------------------------------------ the query

    [Fact]
    public async Task TwoAccountsThatPlayedDifferentGames_NeverSeeEachOthersRows()
    {
        var h = new Harness();
        var a = Owner();
        var b = Owner();
        h.History.Add(Row(a.Id, 111, "Game A", Now.AddHours(-1)));
        h.History.Add(Row(b.Id, 222, "Game B", Now.AddHours(-2)));
        var pa = h.Panel(a);
        var pb = h.Panel(b);

        await pa.RefreshAsync();
        await pb.RefreshAsync();

        Assert.Equal(111, Assert.Single(pa.Recent).Entry.PlaceId);
        Assert.Equal(222, Assert.Single(pb.Recent).Entry.PlaceId);
        Assert.Equal("Game A", pa.LastPlayed!.Name);
        Assert.Equal("Game B", pb.LastPlayed!.Name);
    }

    [Fact]
    public async Task NineDistinctGames_YieldEight_NewestFirst()
    {
        var h = new Harness();
        var o = Owner();
        for (var i = 1; i <= 9; i++) h.History.Add(Row(o.Id, 1000 + i, $"G{i}", Now.AddHours(-i)));
        var p = h.Panel(o);

        await p.RefreshAsync();

        Assert.Equal(AccountPanelViewModel.RecentCap, p.Recent.Count);
        Assert.Equal(1001, p.Recent[0].Entry.PlaceId);
        Assert.DoesNotContain(p.Recent, e => e.Entry.PlaceId == 1009);
    }

    [Fact]
    public async Task APlaceWithManySessions_IsOneEntry_WithTheLatestTimeAndTheSummedDuration()
    {
        var h = new Harness();
        var o = Owner();
        h.History.Add(Row(o.Id, 5, "Pet", Now.AddHours(-5), minutes: 10));
        h.History.Add(Row(o.Id, 5, "Pet", Now.AddHours(-1), minutes: 20));
        var p = h.Panel(o);

        await p.RefreshAsync();

        var e = Assert.Single(p.Recent);
        Assert.Equal(Now.AddHours(-1), e.LastPlayedUtc);
        Assert.Equal(TimeSpan.FromMinutes(30), e.TotalPlayed);
    }

    [Fact]
    public async Task APrivateServerRow_GroupsByServerId_NotByPlace()
    {
        var h = new Harness();
        var o = Owner();
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        h.History.Add(Row(o.Id, 9, "Clan", Now.AddHours(-3), server: s1));
        h.History.Add(Row(o.Id, 9, "Clan", Now.AddHours(-2), server: s2));
        h.History.Add(Row(o.Id, 9, "Clan", Now.AddHours(-1), server: s1));
        h.Library.Add(Server(s1, 9, "Saved server"));
        var p = h.Panel(o);

        await p.RefreshAsync();

        Assert.Equal(2, p.Recent.Count); // same place, two servers
        var first = p.Recent[0];
        Assert.Equal(s1, first.Entry.PrivateServerId);
        Assert.True(first.IsSaved);
        Assert.Equal("Saved server", first.Entry.Name);
        Assert.False(p.Recent[1].IsSaved); // server s2 is not in the Library
    }

    [Fact]
    public async Task ASavedFavourite_IsMarkedSaved_AnUnsavedPlaceIsNot()
    {
        var h = new Harness();
        var o = Owner();
        h.Library.Add(Fav(1, "Saved"));
        h.History.Add(Row(o.Id, 1, "Saved", Now.AddHours(-1)));
        h.History.Add(Row(o.Id, 2, "Unsaved", Now.AddHours(-2)));
        var p = h.Panel(o);

        await p.RefreshAsync();

        Assert.True(p.Recent.Single(e => e.Entry.PlaceId == 1).IsSaved);
        Assert.False(p.Recent.Single(e => e.Entry.PlaceId == 2).IsSaved);
    }

    [Fact]
    public async Task FavouritesAndServers_KeepTheCatalogsOrder_AndCarryThisAccountsAge()
    {
        var h = new Harness();
        var o = Owner();
        var other = Guid.NewGuid();
        var sid = Guid.NewGuid();
        h.Library.Add(Fav(30, "C"));
        h.Library.Add(Fav(10, "A"));
        h.Library.Add(Fav(20, "B"));
        h.Library.Add(Server(sid, 40, "S"));
        h.History.Add(Row(o.Id, 10, "A", Now.AddHours(-4)));
        h.History.Add(Row(other, 30, "C", Now.AddHours(-1))); // someone else's play must not decorate mine
        h.History.Add(Row(o.Id, 40, "S", Now.AddHours(-2), server: sid));
        var p = h.Panel(o);

        await p.RefreshAsync();

        Assert.Equal(new long[] { 30, 10, 20 }, p.Favourites.Select(e => e.Entry.PlaceId).ToArray());
        Assert.Null(p.Favourites[0].LastPlayedUtc);
        Assert.Equal(Now.AddHours(-4), p.Favourites[1].LastPlayedUtc);
        Assert.Equal(Now.AddHours(-2), Assert.Single(p.PrivateServers).LastPlayedUtc);
        Assert.All(p.Favourites, e => Assert.True(e.IsSaved));
    }

    [Fact]
    public async Task LastPlayed_PrefersTheLibrarysRename_AndRowsWithoutAPlaceAreIgnored()
    {
        var h = new Harness();
        var o = Owner();
        h.Library.Add(Fav(1, "Roblox name") with { LocalName = "My name" });
        h.History.Add(Row(o.Id, 1, "Roblox name", Now.AddHours(-3)));
        h.History.Add(new LaunchSession(Guid.NewGuid(), o.Id, "Alt", null, "Following a friend", null, false, Now.AddHours(-2), null, null));
        var p = h.Panel(o);

        await p.RefreshAsync();

        Assert.Equal("My name", p.LastPlayed!.Name);
        Assert.Equal(Now.AddHours(-3), p.LastPlayed.AtUtc);
    }

    [Fact]
    public async Task TheRowsLastPlayedText_ComesFromThePanelObject()
    {
        var h = new Harness();
        var o = Owner();
        h.History.Add(Row(o.Id, 1, "Pet Sim 99", Now.AddHours(-3)));
        var p = h.Panel(o);
        o.AttachPanel(p);
        var raised = new List<string?>();
        o.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        Assert.Equal(string.Empty, o.LastPlayedText);

        await p.RefreshAsync();

        Assert.StartsWith("Pet Sim 99 · ", o.LastPlayedText);
        Assert.Equal(p.LastPlayed!.AtUtc, o.LastPlayedUtc);
        Assert.Contains(nameof(AccountSummary.LastPlayedText), raised);
    }

    [Fact]
    public async Task AFailingStore_LeavesThePanelStanding_AndNeverThrows()
    {
        var o = Owner();
        var p = new AccountPanelViewModel(o, () => throw new NotImplementedException(),
            () => Task.FromResult<IReadOnlyList<LibraryEntry>>([]), _ => Task.CompletedTask, () => Task.CompletedTask);

        await p.RefreshAsync();

        Assert.Empty(p.Recent);
    }

    // ------------------------------------------------------------------ launch

    [Fact]
    public async Task AnEntryAndHome_BothGoThroughTheLaunchDelegate()
    {
        var h = new Harness();
        var o = Owner();
        h.Library.Add(Fav(77, "Fav"));
        var p = h.Panel(o);
        await p.RefreshAsync();

        await p.LaunchAsync(p.Favourites[0]);
        p.OpenHomeCommand.Execute(null);

        Assert.Equal(new LaunchTarget.Place(77), h.Launched[0]);
        Assert.IsType<LaunchTarget.Home>(h.Launched[1]);
        Assert.IsType<LaunchTarget.Home>(p.HomeTarget);
    }

    // ------------------------------------------------------------------ save

    [Fact]
    public async Task Save_UsesTheCachedUniverseId()
    {
        var h = new Harness();
        var o = Owner();
        h.Icons.Known[2] = new GameIconInfo("Unsaved", 20002, "https://example.invalid/2.png", Now);
        h.History.Add(Row(o.Id, 2, "Unsaved", Now.AddHours(-1)));
        var p = h.Panel(o);
        await p.RefreshAsync();
        var entry = Assert.Single(p.Recent);
        Assert.False(entry.IsSaved);

        await p.SaveAsync(entry);

        var added = Assert.Single(h.Favorites.Added);
        Assert.Equal((2L, 20002L), (added.PlaceId, added.UniverseId));
        Assert.Equal(0, h.Icons.ResolveCalls); // known already: no lookup
        Assert.True(Assert.Single(p.Recent).IsSaved);
    }

    [Fact]
    public async Task Save_WithNoCachedRow_ResolvesThroughTheCacheFirst()
    {
        var h = new Harness();
        var o = Owner();
        h.Icons.OnResolve = id => h.Icons.Known[id] = new GameIconInfo("Resolved name", 30003, "https://example.invalid/3.png", Now);
        h.History.Add(Row(o.Id, 3, "Unsaved", Now.AddHours(-1)));
        var p = h.Panel(o);
        await p.RefreshAsync();

        await p.SaveAsync(p.Recent[0]);

        Assert.Equal(1, h.Icons.ResolveCalls);
        var added = Assert.Single(h.Favorites.Added);
        Assert.Equal(30003L, added.UniverseId);
        Assert.Equal("Resolved name", added.Name);
    }

    [Fact]
    public async Task Save_WhenResolutionFails_DoesNothing_AndLeavesTheEntryUnsaved()
    {
        var h = new Harness();
        var o = Owner();
        h.History.Add(Row(o.Id, 4, "Unsaved", Now.AddHours(-1)));
        var p = h.Panel(o);
        await p.RefreshAsync();

        await p.SaveAsync(p.Recent[0]);

        Assert.Equal(1, h.Icons.ResolveCalls);
        Assert.Empty(h.Favorites.Added);
        Assert.False(Assert.Single(p.Recent).IsSaved);
    }

    // ------------------------------------------------------------------ icons: on screen only

    [Fact]
    public async Task ACollapsedPanel_RequestsNoIcons_ExpandingRequestsOnlyTheUncachedUnsavedOnes()
    {
        var h = new Harness();
        var o = Owner(facts: new AccountFacts(1, null, null, Now, null));
        for (var i = 1; i <= 5; i++) h.History.Add(Row(o.Id, 100 + i, $"G{i}", Now.AddHours(-i)));
        h.Icons.Known[101] = new GameIconInfo("G1", 1, "https://example.invalid/1.png", Now); // one cached
        h.Library.Add(Fav(105, "G5")); // one saved (carries its own thumbnail)
        var p = h.Panel(o);

        await p.RefreshAsync();
        Assert.Empty(h.Icons.Requested);

        p.Expand();
        await Task.Delay(50);

        Assert.Equal(new long[] { 102, 103, 104 }, h.Icons.Requested.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task AResolvedIcon_UpdatesTheMatchingEntry_AndACollapsedPanelHearsNothing()
    {
        var h = new Harness();
        var o = Owner(facts: new AccountFacts(1, null, null, Now, null));
        h.History.Add(Row(o.Id, 7, "G7", Now.AddHours(-1)));
        var p = h.Panel(o);
        p.Expand();
        await Task.Delay(50);
        Assert.Null(Assert.Single(p.Recent).Entry.IconUrl);

        h.Icons.Raise(7, new GameIconInfo("G7", 70, "https://example.invalid/7.png", Now));

        Assert.Equal("https://example.invalid/7.png", Assert.Single(p.Recent).Entry.IconUrl);

        p.IsExpanded = false;
        Assert.Equal(0, h.Icons.Subscribers);
    }

    // ------------------------------------------------------------------ expand + facts

    [Fact]
    public void Expand_WithFacts30SecondsOld_DoesNotRequestFacts()
    {
        var h = new Harness();
        var p = h.Panel(Owner(facts: new AccountFacts(1, null, null, DateTimeOffset.UtcNow.AddSeconds(-30), null)));

        p.Expand();

        Assert.Equal(0, h.FactsRequests);
    }

    [Fact]
    public void Expand_WithFacts61SecondsOld_RequestsFacts()
    {
        var h = new Harness();
        var p = h.Panel(Owner(facts: new AccountFacts(1, null, null, DateTimeOffset.UtcNow.AddSeconds(-61), null)));

        p.Expand();

        Assert.Equal(1, h.FactsRequests);
    }

    [Fact]
    public void Expand_WithNoFactsYet_RequestsFacts()
    {
        var h = new Harness();
        var p = h.Panel(Owner());

        p.Expand();

        Assert.Equal(1, h.FactsRequests);
    }

    [Fact]
    public void ResolveExpanded_WritesNothing_RequestsNoFactsAndNoIcons()
    {
        var h = new Harness();
        var o = Owner();
        h.History.Add(Row(o.Id, 1, "G", Now));
        var p = h.Panel(o);
        var toggles = 0;
        p.UserToggled += (_, _) => toggles++;

        p.ResolveExpanded(defaultExpanded: true);

        Assert.True(p.IsExpanded);
        Assert.Equal(0, toggles);
        Assert.Equal(0, h.FactsRequests);
        Assert.Empty(h.Icons.Requested);
    }

    [Fact]
    public void ResolveExpanded_ThePersistedChoiceBeatsTheDefault()
    {
        var h = new Harness();
        var yes = h.Panel(Owner(persisted: true));
        var no = h.Panel(Owner(persisted: false));

        yes.ResolveExpanded(defaultExpanded: false);
        no.ResolveExpanded(defaultExpanded: true);

        Assert.True(yes.IsExpanded);
        Assert.False(no.IsExpanded);
    }

    [Fact]
    public void OnlyAUserToggleRaisesUserToggled()
    {
        var h = new Harness();
        var p = h.Panel(Owner(facts: new AccountFacts(1, null, null, DateTimeOffset.UtcNow, null)));
        var seen = new List<bool>();
        p.UserToggled += (_, v) => seen.Add(v);

        p.Expand();
        p.Expand(); // already open: nothing
        p.IsExpanded = false;

        Assert.Equal(new[] { true, false }, seen);
    }

    // ------------------------------------------------------------------ fakes

    private sealed class InlineUi : IUiDispatcher
    {
        public void Invoke(Action action) => action();
    }

    private sealed class FakeIcons : IGameIconCache
    {
        public readonly Dictionary<long, GameIconInfo> Known = new();
        public readonly List<long> Requested = new();
        public int ResolveCalls;
        public int Subscribers;
        public Action<long>? OnResolve;
        private EventHandler<GameIconResolvedEventArgs>? _handlers;

        public event EventHandler<GameIconResolvedEventArgs>? IconResolved
        {
            add { _handlers += value; Subscribers++; }
            remove { _handlers -= value; Subscribers--; }
        }

        public GameIconInfo? TryGet(long placeId) => Known.TryGetValue(placeId, out var i) ? i : null;
        public bool Request(long placeId) { Requested.Add(placeId); return true; }
        public Task<bool> ResolveAsync(long placeId, CancellationToken ct = default)
        {
            ResolveCalls++;
            OnResolve?.Invoke(placeId);
            return Task.FromResult(Known.ContainsKey(placeId));
        }
        public void AttachQueue(Func<long, bool> enqueue) { }
        public void Raise(long placeId, GameIconInfo info) => _handlers?.Invoke(this, new GameIconResolvedEventArgs(placeId, info));
    }

    private sealed class FakeFavorites : IFavoriteGameStore
    {
        public readonly List<(long PlaceId, long UniverseId, string Name, string Thumb)> Added = new();
        public event EventHandler? DefaultChanged { add { } remove { } }
        public Task<IReadOnlyList<FavoriteGame>> ListAsync() => Task.FromResult<IReadOnlyList<FavoriteGame>>([]);
        public Task<FavoriteGame?> GetDefaultAsync() => Task.FromResult<FavoriteGame?>(null);
        public Task<FavoriteGame> AddAsync(long placeId, long universeId, string name, string thumbnailUrl)
        {
            Added.Add((placeId, universeId, name, thumbnailUrl));
            return Task.FromResult(new FavoriteGame(placeId, universeId, name, thumbnailUrl, false, DateTimeOffset.UtcNow));
        }
        public Task RemoveAsync(long placeId) => Task.CompletedTask;
        public Task SetDefaultAsync(long placeId) => Task.CompletedTask;
        public Task ClearDefaultAsync() => Task.CompletedTask;
        public Task UpdateLocalNameAsync(long placeId, string? localName) => Task.CompletedTask;
    }
}
