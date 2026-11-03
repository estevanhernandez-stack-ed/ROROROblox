using System.Globalization;
using ROROROblox.App.Games;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Tests.Rendering;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 9: what the Library page binds to, without WPF. The in-memory stores complete synchronously,
/// so every <c>await</c> here stays on the test's thread. Item 8's <c>LibraryViewModelTests</c> covers the
/// rules (chips, paste routing, Play resolution); this covers the page's own surface: the worded items,
/// the chip and layout bools the controls bind, the empty and no-match flags, the remove confirm, and the
/// "icons only for tiles on screen" rule.
/// </summary>
[Collection("MutatesUiCulture")]
public class LibraryPageViewModelTests : IDisposable
{
    private readonly CultureInfo _prev = TranslationSource.Instance.CurrentCulture;
    private readonly LibraryPageFixtures.MemFavorites _favourites = new();
    private readonly LibraryPageFixtures.MemServers _servers = new();
    private readonly Dictionary<long, GameStat> _games = [];
    private readonly CountingIcons _icons = new();
    private readonly MainViewModelTests.FakeAppSettings _settings = new();
    private readonly List<(Guid Account, LaunchTarget Target)> _launched = [];
    private IReadOnlyList<AccountSummary> _roster = [];
    private int _libraryChanged;

    public LibraryPageViewModelTests() => TranslationSource.Instance.CurrentCulture = new CultureInfo("en");

    public void Dispose() => TranslationSource.Instance.CurrentCulture = _prev;

    private LibraryViewModel Vm(IUiDispatcher? ui = null) => new(
        new LibraryCatalog(_favourites, _servers, new LibraryPageFixtures.MemStats(_games), new LibraryPageFixtures.MemHistory(), _icons),
        _favourites, _servers, new LibraryPageFixtures.NullApi(), _settings,
        _ => Task.FromResult<LaunchTarget?>(null),
        () => _roster,
        (id, t) => { _launched.Add((id, t)); return Task.CompletedTask; },
        libraryChanged: () => _libraryChanged++,
        icons: _icons, ui: ui);

    private static FavoriteGame Fav(long place, string name, bool isDefault = false, string thumb = "")
        => new(place, place * 10, name, thumb, isDefault, DateTimeOffset.UtcNow);

    private async Task<LibraryViewModel> LoadedAsync()
    {
        var vm = Vm();
        await vm.LoadAsync();
        return vm;
    }

    // ---- the worded items ----

    [Fact]
    public async Task Items_FollowVisible_AndKeepTheirInstancesAcrossKeystrokes()
    {
        for (var i = 1; i <= 6; i++) _favourites.Items.Add(Fav(i, i % 2 == 0 ? $"Obby {i}" : $"Tycoon {i}"));
        var vm = await LoadedAsync();
        Assert.Equal(6, vm.Items.Count);
        var before = vm.Items.ToList();

        vm.Query = "obby";
        Assert.Equal([2L, 4L, 6L], vm.Items.Select(i => i.PlaceId).ToArray());
        Assert.All(vm.Items, i => Assert.Contains(i, before)); // the same objects, so the realised tiles stay

        vm.Query = "";
        Assert.Equal([1L, 2L, 3L, 4L, 5L, 6L], vm.Items.Select(i => i.PlaceId).OrderBy(x => x).ToArray());
        Assert.Equal(before.Count, vm.Items.Count);
        Assert.All(before, b => Assert.Contains(b, vm.Items));
    }

    [Fact]
    public async Task AnItem_WordsItsFacts_FromTheEntry_NotFromTheView()
    {
        var now = DateTimeOffset.UtcNow;
        _favourites.Items.Add(Fav(1, "Petal Simulator", isDefault: true));
        _favourites.Items.Add(Fav(2, "Adopt Pals"));
        _games[1] = new GameStat("Petal Simulator", 3, TimeSpan.FromHours(61), now.AddHours(-2));
        var vm = await LoadedAsync();

        var played = vm.Items.Single(i => i.PlaceId == 1);
        Assert.True(played.IsDefault);
        Assert.Equal("Petal Simulator", played.DisplayName);
        Assert.Equal("PS", played.Initials);
        Assert.Equal("61 hr", played.TotalText);
        Assert.Equal($"{played.LastPlayedText} · 61 hr", played.MetaText);
        Assert.Equal("Play Petal Simulator", played.PlayName);
        Assert.Equal("Play Petal Simulator as another account", played.PlayAsName);

        var never = vm.Items.Single(i => i.PlaceId == 2);
        Assert.Equal("Never played", never.MetaText);
        Assert.Equal(string.Empty, never.TotalText);
        Assert.False(never.IsDefault);
        Assert.False(never.IsPrivate);
    }

    [Fact]
    public async Task OnlySavedEntriesOfferManagement_AndARecentPlaceOffersNone()
    {
        _favourites.Items.Add(Fav(1, "Saved Game", isDefault: true));
        _games[9] = new GameStat("Played Only", 1, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow);
        var vm = await LoadedAsync();

        var saved = vm.Items.Single(i => i.PlaceId == 1);
        Assert.True(saved.IsSaved);
        Assert.False(saved.CanSetDefault);   // already the default
        Assert.True(saved.CanClearDefault);

        var recent = vm.Items.Single(i => i.PlaceId == 9);
        Assert.False(recent.IsSaved);
        Assert.False(recent.CanSetDefault);
        Assert.False(recent.CanResetName);
        vm.RequestRemove(recent);
        Assert.False(vm.IsRemoveConfirmOpen); // there is nothing to remove
    }

    [Fact]
    public async Task ARename_UpdatesTheSameItemInPlace()
    {
        _favourites.Items.Add(Fav(1, "Petal Simulator"));
        var vm = await LoadedAsync();
        var item = vm.Items.Single();
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.RenameAsync(item.Entry, "Flower Farm");

        Assert.Same(item, vm.Items.Single());
        Assert.Equal("Flower Farm", item.DisplayName);
        Assert.True(item.CanResetName);
        Assert.Contains(string.Empty, changed); // "everything changed", so the tile and the row re-read their words
        Assert.Equal(1, _libraryChanged);
    }

    // ---- the bools the controls bind ----

    [Fact]
    public async Task ChipBools_ReadAndWriteTheFlags_AndRaiseThemselves()
    {
        _favourites.Items.Add(Fav(1, "A"));
        var vm = await LoadedAsync();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.False(vm.IsRecentOn);
        vm.IsRecentOn = true;
        vm.IsPrivateServersOn = true;

        Assert.Equal(LibraryFilter.Recent | LibraryFilter.PrivateServers, vm.Filters);
        Assert.True(vm.IsRecentOn);
        Assert.False(vm.IsFavouritesOn);
        Assert.Contains(nameof(LibraryViewModel.IsRecentOn), raised);
        Assert.Contains(nameof(LibraryViewModel.IsPrivateServersOn), raised);

        vm.IsRecentOn = false;
        Assert.Equal(LibraryFilter.PrivateServers, vm.Filters);
        await vm.PersistenceSettled;
        Assert.Equal("PrivateServers", _settings.LibraryFilters);
    }

    [Fact]
    public async Task LayoutBools_AreARadioPair_AndUncheckingChangesNothing()
    {
        var vm = await LoadedAsync();
        Assert.True(vm.IsGridView);
        Assert.False(vm.IsListView);

        vm.IsListView = true;
        Assert.Equal(LibraryView.List, vm.View);
        Assert.False(vm.IsGridView);

        vm.IsGridView = false; // the radio group un-checking Grid when List is picked writes false here
        Assert.Equal(LibraryView.List, vm.View);

        vm.IsGridView = true;
        Assert.Equal(LibraryView.Grid, vm.View);
        await vm.PersistenceSettled;
        Assert.Equal("grid", _settings.LibraryView);
    }

    [Fact]
    public async Task Shape_TheInvitation_TheNoMatchLine_AndTheRobloxGroup_AreMutuallyHonest()
    {
        var vm = await LoadedAsync();
        Assert.True(vm.ShowEmptyInvitation);   // nothing saved, nothing played
        Assert.False(vm.ShowNoMatch);

        _favourites.Items.Add(Fav(1, "Petal Simulator"));
        await vm.ReloadAsync();
        Assert.False(vm.ShowEmptyInvitation);
        Assert.False(vm.ShowNoMatch);

        vm.Query = "zzz";                       // entries exist, the box hides them all
        Assert.False(vm.ShowEmptyInvitation);
        Assert.True(vm.ShowNoMatch);

        vm.FromRoblox.Add(new GameSearchResult(5, 50, "Found Game", "Maker", 1234, ""));
        Assert.True(vm.HasFromRoblox);
        Assert.False(vm.ShowNoMatch);           // results are showing, so the line steps aside
        var row = Assert.Single(vm.FromRobloxItems);
        Assert.Equal("by Maker", row.CreatorText);
        Assert.Equal("1,234 playing", row.PlayersText);
        Assert.Equal("Add Found Game to the Library", row.AddName);

        vm.Query = "z";                          // the text changed: the results answered an older question
        Assert.Empty(vm.FromRoblox);
        Assert.Empty(vm.FromRobloxItems);
    }

    // ---- the remove confirm ----

    [Fact]
    public async Task Remove_AsksFirst_CancelKeepsIt_ConfirmRemovesIt()
    {
        _favourites.Items.Add(Fav(1, "Petal Simulator"));
        _favourites.Items.Add(Fav(2, "Sprout Garden"));
        var vm = await LoadedAsync();
        var item = vm.Items.Single(i => i.PlaceId == 1);

        vm.RequestRemove(item);
        Assert.True(vm.IsRemoveConfirmOpen);
        Assert.Equal("Remove Petal Simulator from your saved games?", vm.RemoveConfirmText);
        Assert.Equal(2, _favourites.Items.Count);   // asking removes nothing

        vm.CancelRemove();
        Assert.False(vm.IsRemoveConfirmOpen);
        Assert.Equal(2, _favourites.Items.Count);

        vm.RequestRemove(item);
        await vm.ConfirmRemoveAsync();
        Assert.False(vm.IsRemoveConfirmOpen);
        Assert.Equal([2L], _favourites.Items.Select(f => f.PlaceId).ToArray());
        Assert.Equal([2L], vm.Items.Select(i => i.PlaceId).ToArray());
        Assert.Equal("Removed.", vm.StatusText);
        Assert.Equal(1, _libraryChanged);

        await vm.ConfirmRemoveAsync(); // nothing pending: a double click cannot remove twice
        Assert.Single(_favourites.Items);
    }

    [Fact]
    public async Task RemovingAServer_AsksInServerWords()
    {
        _servers.Items.Add(new SavedPrivateServer(Guid.NewGuid(), 7, "FAKE-LINK-CODE", PrivateServerCodeKind.LinkCode, "Clan Base", "Petal Simulator", "", DateTimeOffset.UtcNow, null));
        var vm = await LoadedAsync();
        var item = vm.Items.Single();
        Assert.True(item.IsPrivate);

        vm.RequestRemove(item);

        Assert.Equal("Remove Clan Base from your saved servers?", vm.RemoveConfirmText);
        await vm.ConfirmRemoveAsync();
        Assert.Empty(_servers.Items);
    }

    // ---- Play with no account ----

    [Fact]
    public async Task PlayWithNoAccount_SaysSoInsteadOfDoingNothing()
    {
        _favourites.Items.Add(Fav(1, "Petal Simulator"));
        var vm = await LoadedAsync();

        var launched = await vm.PlayAsync(vm.Items.Single().Entry, null);

        Assert.False(launched);
        Assert.Empty(_launched);
        Assert.Equal("Add an account first, then play.", vm.StatusText);
    }

    // ---- icons: only a tile on screen asks, only for itself ----

    [Fact]
    public async Task Loading100Entries_RequestsNoIcons_AndATileOnScreenRequestsOnlyItsOwn()
    {
        for (var i = 1; i <= 100; i++) _favourites.Items.Add(Fav(i, $"Game {i}"));
        var vm = await LoadedAsync();
        Assert.Equal(100, vm.Items.Count);
        Assert.Empty(_icons.Requested);                        // nothing fetched for places nobody can see

        vm.NoteOnScreen(vm.Items[3]);
        Assert.Equal([vm.Items[3].PlaceId], _icons.Requested); // the one realised tile, nothing else
    }

    [Fact]
    public async Task ATileWithAnIcon_AsksForNothing()
    {
        _favourites.Items.Add(Fav(1, "Has Icon", thumb: "https://example.invalid/icon.png"));
        var vm = await LoadedAsync();

        vm.NoteOnScreen(vm.Items.Single());

        Assert.Empty(_icons.Requested);
    }

    [Fact]
    public async Task AResolvedIcon_ReachesTheTile_AndStopsAfterDetach()
    {
        _favourites.Items.Add(Fav(1, "No Icon Yet"));
        _favourites.Items.Add(Fav(2, "Another"));
        var ran = 0;
        var vm = Vm(new CountingDispatcher(() => ran++));
        await vm.LoadAsync();
        var item = vm.Items.Single(i => i.PlaceId == 1);
        Assert.Null(item.IconUrl);
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        _icons.Resolve(1, "https://example.invalid/one.png");

        Assert.Equal("https://example.invalid/one.png", item.IconUrl);
        Assert.Null(vm.Items.Single(i => i.PlaceId == 2).IconUrl);
        Assert.Contains(nameof(LibraryItem.IconUrl), raised);
        Assert.Equal(1, ran);                                    // marshalled through the UI dispatcher

        vm.Detach();
        _icons.Resolve(2, "https://example.invalid/two.png");
        Assert.Null(vm.Items.Single(i => i.PlaceId == 2).IconUrl); // the page left the screen: it no longer listens
    }

    private sealed class CountingDispatcher(Action onInvoke) : IUiDispatcher
    {
        public void Invoke(Action action) { onInvoke(); action(); }
    }

    private sealed class CountingIcons : IGameIconCache
    {
        public List<long> Requested { get; } = [];
        public event EventHandler<GameIconResolvedEventArgs>? IconResolved;
        public GameIconInfo? TryGet(long placeId) => null;
        public bool Request(long placeId) { Requested.Add(placeId); return true; }
        public Task<bool> ResolveAsync(long placeId, CancellationToken ct = default) => Task.FromResult(false);
        public void AttachQueue(Func<long, bool> enqueue) { }
        public void Resolve(long placeId, string url)
            => IconResolved?.Invoke(this, new GameIconResolvedEventArgs(placeId, new GameIconInfo("n", placeId * 10, url, DateTimeOffset.UtcNow)));
    }
}
