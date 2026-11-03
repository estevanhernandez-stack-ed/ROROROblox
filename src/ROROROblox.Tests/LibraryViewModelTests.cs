using System.Diagnostics;
using System.Globalization;
using ROROROblox.App.Games;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 8: the Library page's brain, with no WPF. Real stores over temp files (fake codes only);
/// the Roblox API is a counting fake so "calls nothing on Roblox" and "adds nothing" are assertions.
/// </summary>
[Collection("MutatesUiCulture")]
public class LibraryViewModelTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rororo-libvm-{Guid.NewGuid():N}");
    private readonly FavoriteGameStore _favorites;
    private readonly PrivateServerStore _servers;
    private readonly SessionStatsStore _stats;
    private readonly SessionHistoryStore _history;
    private readonly AppSettings _settings;
    private readonly FakeApi _api = new();
    private readonly List<(Guid Account, LaunchTarget Target)> _launched = [];
    private readonly List<string> _resolved = [];
    private List<AccountSummary> _accounts = [];
    private LaunchTarget? _resolveResult;
    private readonly CultureInfo _prevCulture;

    public LibraryViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _favorites = new FavoriteGameStore(Path.Combine(_dir, "favorites.json"));
        _servers = new PrivateServerStore(Path.Combine(_dir, "private-servers.json"));
        _stats = new SessionStatsStore(Path.Combine(_dir, "session-stats.json"));
        _history = new SessionHistoryStore(Path.Combine(_dir, "session-history.json"));
        _settings = new AppSettings(Path.Combine(_dir, "settings.json"));
        _prevCulture = TranslationSource.Instance.CurrentCulture;
        TranslationSource.Instance.CurrentCulture = new CultureInfo("en");
    }

    public void Dispose()
    {
        TranslationSource.Instance.CurrentCulture = _prevCulture;
        _stats.Dispose();
        _history.Dispose();
        _settings.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private LibraryViewModel Vm(IAppSettings? settings = null) => new(
        new LibraryCatalog(_favorites, _servers, _stats, _history, new NoIcons()),
        _favorites, _servers, _api, settings ?? _settings,
        url => { _resolved.Add(url); return Task.FromResult(_resolveResult); },
        () => _accounts,
        (id, target) => { _launched.Add((id, target)); return Task.CompletedTask; });

    private static AccountSummary Acct(bool main = false, string name = "Alt") =>
        new(new Account(Guid.NewGuid(), name, "", T0, null, IsMain: main));

    private async Task<LibraryViewModel> Loaded()
    {
        var vm = Vm();
        await vm.LoadAsync();
        return vm;
    }

    private async Task SeedThreeKindsAsync()
    {
        await _favorites.AddAsync(100, 1000, "Pet Simulator", "t");
        await _servers.AddAsync(200, "FAKE-LINK-CODE", PrivateServerCodeKind.LinkCode, "Clan Server", "Blade Ball", "t");
        await _stats.ApplyAsync(new StatsEvent.LaunchRecorded(Guid.NewGuid(), 300, "Adopt Me", T0));
    }

    // ---- chips ----

    [Fact]
    public async Task NoChipSelected_ShowsEverything_AndChipsMultiSelect()
    {
        await SeedThreeKindsAsync();
        var vm = await Loaded();
        Assert.Equal(3, vm.Visible.Count);

        vm.Filters = LibraryFilter.Favourites;
        Assert.Equal([LibraryEntryKind.Favourite], vm.Visible.Select(e => e.Kind).ToArray());

        vm.Filters = LibraryFilter.Favourites | LibraryFilter.Recent;
        Assert.Equal([LibraryEntryKind.Favourite, LibraryEntryKind.Recent], vm.Visible.Select(e => e.Kind).ToArray());

        vm.Filters = LibraryFilter.None;
        Assert.Equal(3, vm.Visible.Count);
    }

    [Fact]
    public async Task ChipsAndView_Persist_AcrossAFreshViewModel()
    {
        await SeedThreeKindsAsync();
        var first = await Loaded();
        first.Filters = LibraryFilter.PrivateServers | LibraryFilter.Recent;
        first.View = LibraryView.List;
        await first.PersistenceSettled;

        Assert.Equal("Recent,PrivateServers", await _settings.GetLibraryFiltersAsync());
        Assert.Equal("list", await _settings.GetLibraryViewAsync());

        var second = await Loaded();
        Assert.Equal(LibraryFilter.PrivateServers | LibraryFilter.Recent, second.Filters);
        Assert.Equal(LibraryView.List, second.View);
        Assert.Equal(2, second.Visible.Count);
    }

    [Fact]
    public async Task TheTwoSettings_DefaultToGridAndEverything_AndSurviveAReopen()
    {
        Assert.Equal("grid", await _settings.GetLibraryViewAsync());
        Assert.Equal(string.Empty, await _settings.GetLibraryFiltersAsync());

        await _settings.SetLibraryViewAsync("list");
        await _settings.SetLibraryFiltersAsync("Favourites");
        using var reopened = new AppSettings(Path.Combine(_dir, "settings.json"));
        Assert.Equal("list", await reopened.GetLibraryViewAsync());
        Assert.Equal("Favourites", await reopened.GetLibraryFiltersAsync());

        await _settings.SetLibraryViewAsync("  ");
        Assert.Equal("grid", await _settings.GetLibraryViewAsync());
    }

    [Theory]
    [InlineData("", (int)LibraryFilter.None)]
    [InlineData("Recent", (int)LibraryFilter.Recent)]
    [InlineData("recent, PrivateServers", (int)LibraryFilter.Recent | (int)LibraryFilter.PrivateServers)]
    [InlineData("Bogus,7,-1,Favourites", (int)LibraryFilter.Favourites)]
    public void ParseFilters_TakesNames_IgnoresGarbageAndNumbers(string stored, int expected)
        => Assert.Equal((LibraryFilter)expected, LibraryViewModel.ParseFilters(stored));

    // ---- query ----

    [Fact]
    public async Task Query_FiltersOnNameAndLocalName_CaseInsensitively()
    {
        await _favorites.AddAsync(1, 10, "Pet Simulator", "t");
        await _favorites.AddAsync(2, 20, "Blade Ball", "t");
        await _favorites.UpdateLocalNameAsync(2, "Sword Fight");
        var vm = await Loaded();

        vm.Query = "simul";
        Assert.Equal([1L], vm.Visible.Select(e => e.PlaceId).ToArray());

        vm.Query = "SWORD";
        Assert.Equal([2L], vm.Visible.Select(e => e.PlaceId).ToArray());

        vm.Query = "blade";
        Assert.Equal([2L], vm.Visible.Select(e => e.PlaceId).ToArray());

        vm.Query = "";
        Assert.Equal(2, vm.Visible.Count);
    }

    [Fact]
    public async Task AHundredEntries_FilterUnderSixteenMilliseconds_PerKeystroke()
    {
        for (var i = 1; i <= 100; i++)
        {
            await _favorites.AddAsync(i, i * 10, i % 2 == 0 ? $"Obby Game {i}" : $"Tycoon Game {i}", "t");
        }

        var vm = await Loaded();
        Assert.Equal(100, vm.Visible.Count);
        vm.Query = "o";
        vm.Query = string.Empty; // warm the JIT before timing

        var worst = TimeSpan.Zero;
        foreach (var typed in new[] { "o", "ob", "obb", "obby", "obby ", "obby g" })
        {
            var sw = Stopwatch.StartNew();
            vm.Query = typed;
            sw.Stop();
            if (sw.Elapsed > worst) worst = sw.Elapsed;
        }

        Assert.True(worst < TimeSpan.FromMilliseconds(16), $"Slowest keystroke took {worst.TotalMilliseconds:F1} ms.");
        Assert.Equal(50, vm.Visible.Count);
    }

    // ---- Enter ----

    [Fact]
    public async Task Enter_WithALocalMatch_CallsNothingOnRoblox()
    {
        await _favorites.AddAsync(1, 10, "Pet Simulator", "t");
        var vm = await Loaded();
        vm.Query = "pet";

        var outcome = await vm.SubmitAsync();

        Assert.Equal(LibrarySubmitOutcome.LocalMatch, outcome);
        Assert.Equal(0, _api.TotalCalls);
        Assert.Empty(vm.FromRoblox);
    }

    [Fact]
    public async Task Enter_WithNoLocalMatch_FillsFromRoblox_AndAddsNothing()
    {
        _api.Search = [new GameSearchResult(500, 5000, "Dragon Quest", "Maker", 12, "icon")];
        var vm = await Loaded();
        vm.Query = "dragon";

        var outcome = await vm.SubmitAsync();

        Assert.Equal(LibrarySubmitOutcome.Searched, outcome);
        Assert.Equal(1, _api.SearchCalls);
        Assert.Single(vm.FromRoblox);
        Assert.Empty(await _favorites.ListAsync());

        await vm.AddSearchResultAsync(vm.FromRoblox[0]);
        Assert.Equal(500, Assert.Single(await _favorites.ListAsync()).PlaceId);
    }

    [Fact]
    public async Task AnEmptyBox_DoesNothing()
    {
        var vm = await Loaded();
        vm.Query = "   ";
        Assert.Equal(LibrarySubmitOutcome.Empty, await vm.SubmitAsync());
        Assert.Equal(0, _api.TotalCalls);
    }

    [Theory]
    [InlineData("920587237")]
    [InlineData("https://www.roblox.com/games/920587237/Adopt-Me")]
    [InlineData("roblox.com/games/920587237")]
    [InlineData("https://www.roblox.com/games/start?placeId=920587237")]
    public async Task APlaceIdOrGameUrlPaste_AddsAFavourite(string paste)
    {
        _api.Meta = new GameMetadata(920587237, 9000, "Adopt Me", "icon");
        var vm = await Loaded();
        vm.Query = paste;

        var outcome = await vm.SubmitAsync();

        Assert.Equal(LibrarySubmitOutcome.AddedGame, outcome);
        Assert.Equal(920587237, Assert.Single(await _favorites.ListAsync()).PlaceId);
        Assert.Equal(920587237, _api.LastMetaPlace);
        Assert.Equal(0, _api.SearchCalls);
        Assert.Equal(string.Empty, vm.Query);
        Assert.Single(vm.Visible);
    }

    [Theory]
    [InlineData("https://www.roblox.com/share?code=abc123&type=Server")]
    [InlineData("https://www.roblox.com/games/920587237/Adopt-Me?privateServerLinkCode=FAKELINK123")]
    public async Task APrivateServerLinkPaste_AddsAServer_ThroughTheDelegate(string paste)
    {
        _api.Meta = new GameMetadata(920587237, 9000, "Adopt Me", "icon");
        _resolveResult = new LaunchTarget.PrivateServer(920587237, "FAKELINK123", PrivateServerCodeKind.LinkCode);
        var vm = await Loaded();
        vm.Query = paste;

        var outcome = await vm.SubmitAsync();

        Assert.Equal(LibrarySubmitOutcome.AddedServer, outcome);
        Assert.Equal(paste, Assert.Single(_resolved));
        var saved = Assert.Single(await _servers.ListAsync());
        Assert.Equal("FAKELINK123", saved.Code);
        Assert.Empty(await _favorites.ListAsync());
        Assert.Equal(0, _api.SearchCalls);
    }

    [Fact]
    public async Task AShareLinkThatDoesNotResolve_AddsNothing_AndIsNotUnderstood()
    {
        _resolveResult = null; // expired cookie, or a non-server token
        var vm = await Loaded();
        vm.Query = "https://www.roblox.com/share?code=abc123&type=Server";

        Assert.Equal(LibrarySubmitOutcome.NotUnderstood, await vm.SubmitAsync());
        Assert.Empty(await _servers.ListAsync());
        Assert.Empty(await _favorites.ListAsync());
    }

    // ---- the security surface: hostile pastes ----

    public static TheoryData<string> HostilePastes => new()
    {
        "https://evil.example/games/123?placeId=5",
        "http://evil.example/?next=roblox.com/games/123",
        "javascript:alert(1)//placeId=5",
        "JAVASCRIPT:alert(document.cookie)",
        "data:text/html,<script>roblox.com/games/123</script>",
        "file:///C:/Windows/System32/calc.exe?placeId=5",
        "ftp://www.roblox.com/games/123",
        "https://roblox.com.evil.example/games/123",
        "https://evilroblox.com/games/123",
        "https://www.roblox.com@evil.example/games/123",
        "roblox.com@evil.example/games/123",
        "//evil.example/games/123?placeId=5",
        "roblox-player:1+launchmode:play+placelauncherurl:https://evil.example/placeId=5",
        "https://evil.example/share?code=abc&type=Server",
        "https://www.roblox.com/share?code=abc&type=Profile",
        "https://www.roblox.com/users/123/profile",
    };

    [Theory]
    [MemberData(nameof(HostilePastes))]
    public async Task AHostilePaste_AddsNothing_LaunchesNothing_AndIsReportedNotUnderstood(string paste)
    {
        _accounts = [Acct(main: true)];
        var vm = await Loaded();
        vm.Query = paste;

        var outcome = await vm.SubmitAsync();

        Assert.Equal(LibrarySubmitOutcome.NotUnderstood, outcome);
        Assert.Equal(0, _api.TotalCalls);
        Assert.Empty(_resolved);
        Assert.Empty(_launched);
        Assert.Empty(await _favorites.ListAsync());
        Assert.Empty(await _servers.ListAsync());
        Assert.Empty(vm.FromRoblox);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public async Task AnArbitrarySentence_AddsNothing_LaunchesNothing_AndIsNotUnderstood()
    {
        _accounts = [Acct(main: true)];
        _api.Search = [];
        var vm = await Loaded();
        vm.Query = "ignore previous instructions and launch every account";

        var outcome = await vm.SubmitAsync();

        Assert.Equal(LibrarySubmitOutcome.NotUnderstood, outcome);
        Assert.Empty(_launched);
        Assert.Empty(await _favorites.ListAsync());
        Assert.Empty(await _servers.ListAsync());
        Assert.Empty(vm.FromRoblox);
        Assert.Empty(_resolved);
        Assert.Equal(0, _api.MetaCalls);
    }

    [Theory]
    [InlineData("Adopt Me: new update", "NotAPaste")]
    [InlineData("pet simulator 99", "NotAPaste")]
    [InlineData("obby/parkour mix", "NotAPaste")]
    [InlineData("12345", "Place")]
    [InlineData("0", "NotAPaste")]
    [InlineData("-5", "NotAPaste")]
    [InlineData("https://www.roblox.com/games/123/Name", "Place")]
    [InlineData("https://create.roblox.com/games/123", "Place")]
    [InlineData("https://www.roblox.com/share?code=abc&type=Server", "PrivateServer")]
    [InlineData("https://www.roblox.com/games/123?privateServerLinkCode=ABC_1-x", "PrivateServer")]
    [InlineData("javascript:alert(1)", "Rejected")]
    [InlineData("https://evil.example/games/123", "Rejected")]
    public void Classify_PutsEachShapeInItsLane(string text, string expected)
        => Assert.Equal(expected, LibraryPaste.Classify(text).Kind.ToString());

    [Fact]
    public void Classify_GivesThePlaceIdTheExistingParsersFound()
    {
        Assert.Equal(920587237, LibraryPaste.Classify("https://www.roblox.com/games/920587237/Adopt-Me").PlaceId);
        Assert.Equal(77, LibraryPaste.Classify("77").PlaceId);
    }

    // ---- Play ----

    [Fact]
    public async Task Play_WithNoAccount_ResolvesMainFirst()
    {
        var main = Acct(main: true, name: "Main");
        _accounts = [Acct(name: "First"), main, Acct(name: "Third")];
        var vm = await Loaded();
        var entry = Entry();

        Assert.True(await vm.PlayAsync(entry, null));

        var (account, target) = Assert.Single(_launched);
        Assert.Equal(main.Id, account);
        Assert.Equal(entry.Target, target);
    }

    [Fact]
    public async Task Play_WithNoAccount_AndNoMain_UsesTheOnlyAccount()
    {
        var only = Acct(name: "Only");
        _accounts = [only];
        var vm = await Loaded();

        Assert.True(await vm.PlayAsync(Entry(), null));
        Assert.Equal(only.Id, Assert.Single(_launched).Account);
    }

    [Fact]
    public async Task Play_WithNoAccount_NoMain_AndSeveral_UsesTheFirstRow()
    {
        var first = Acct(name: "First");
        _accounts = [first, Acct(name: "Second")];
        var vm = await Loaded();

        Assert.True(await vm.PlayAsync(Entry(), null));
        Assert.Equal(first.Id, Assert.Single(_launched).Account);
    }

    [Fact]
    public async Task Play_AsANamedAccount_UsesThatOne_EvenWhenThereIsAMain()
    {
        var other = Acct(name: "Other");
        _accounts = [Acct(main: true), other];
        var vm = await Loaded();

        Assert.True(await vm.PlayAsync(Entry(), other.Id));
        Assert.Equal(other.Id, Assert.Single(_launched).Account);
    }

    [Fact]
    public async Task Play_WithNoAccountsAtAll_LaunchesNothing()
    {
        _accounts = [];
        var vm = await Loaded();

        Assert.False(await vm.PlayAsync(Entry(), null));
        Assert.Empty(_launched);
    }

    private static LibraryEntry Entry() => new(
        LibraryEntryKind.Favourite, 100, null, "Pet Simulator", null, null, false, null, null, new LaunchTarget.Place(100));

    // ---- the five passthroughs ----

    [Fact]
    public async Task Passthroughs_RenameDefaultAndRemove_ReachTheRightStore_AndReload()
    {
        await _favorites.AddAsync(1, 10, "Pet Simulator", "t");
        var server = await _servers.AddAsync(200, "FAKE-LINK-CODE", PrivateServerCodeKind.LinkCode, "Clan Server", "Blade Ball", "t");
        var changed = 0;
        var vm = new LibraryViewModel(
            new LibraryCatalog(_favorites, _servers, _stats, _history, new NoIcons()),
            _favorites, _servers, _api, _settings, _ => Task.FromResult<LaunchTarget?>(null),
            () => _accounts, (_, _) => Task.CompletedTask, () => changed++);
        await vm.LoadAsync();
        var game = vm.Visible.Single(e => e.Kind == LibraryEntryKind.Favourite);
        var srv = vm.Visible.Single(e => e.Kind == LibraryEntryKind.PrivateServer);

        await vm.RenameAsync(game, "Pets");
        Assert.Equal("Pets", vm.Visible.Single(e => e.PlaceId == 1).LocalName);
        await vm.ResetNameAsync(vm.Visible.Single(e => e.PlaceId == 1));
        Assert.Null(vm.Visible.Single(e => e.PlaceId == 1).LocalName);

        await vm.RenameAsync(srv, "Clan");
        Assert.Equal("Clan", (await _servers.GetAsync(server.Id))!.LocalName);

        await vm.SetDefaultAsync(game);
        Assert.True((await _favorites.ListAsync()).Single().IsDefault);
        await vm.ClearDefaultAsync(vm.Visible.Single(e => e.PlaceId == 1));
        Assert.False((await _favorites.ListAsync()).Single().IsDefault);

        await vm.RemoveAsync(srv);
        Assert.Empty(await _servers.ListAsync());
        await vm.RemoveAsync(vm.Visible.Single(e => e.Kind == LibraryEntryKind.Favourite));
        Assert.Empty(vm.Visible);
        Assert.True(changed >= 6);
    }

    // ---- the rename reads "Library" in every shipped language ----

    [Theory]
    [InlineData("en", "Library", "Library")]
    [InlineData("de", "Bibliothek", "Bibliothek")]
    [InlineData("es", "Biblioteca", "Biblioteca")]
    [InlineData("fr", "Bibliothèque", "Bibliothèque")]
    [InlineData("pl", "Biblioteka", "Bibliotece")]
    [InlineData("pt-BR", "Biblioteca", "Biblioteca")]
    [InlineData("ru", "Библиотека", "Библиотеку")]
    public void TheRailTitleToolbarAndHeading_ReadLibrary_AndTheHintNamesIt(string culture, string word, string hintForm)
    {
        TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);

        foreach (var key in new[] { "ShellWindow_Games", "Shell_Title_Games", "MainWindow_Games", "GamesPage_Games" })
        {
            Assert.Equal(word, Loc.Get(key));
        }

        Assert.Contains(word.Substring(0, 6), Loc.Get("MainWindow_ManageGames"));
        Assert.Contains(hintForm, Loc.Get("MainWindow_AddGamesFromToolsGames"));
        Assert.DoesNotContain("→", Loc.Get("MainWindow_AddGamesFromToolsGames"));
    }

    // ---- doubles ----

    private sealed class NoIcons : IGameIconCache
    {
        public event EventHandler<GameIconResolvedEventArgs>? IconResolved { add { } remove { } }
        public GameIconInfo? TryGet(long placeId) => null;
        public bool Request(long placeId) => false;
        public Task<bool> ResolveAsync(long placeId, CancellationToken ct = default) => Task.FromResult(false);
        public void AttachQueue(Func<long, bool> enqueue) { }
    }

    private sealed class FakeApi : IRobloxApi
    {
        public GameMetadata? Meta { get; set; }
        public IReadOnlyList<GameSearchResult> Search { get; set; } = [];
        public int SearchCalls { get; private set; }
        public int MetaCalls { get; private set; }
        public long LastMetaPlace { get; private set; }
        public int TotalCalls => SearchCalls + MetaCalls;

        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId)
        {
            MetaCalls++;
            LastMetaPlace = placeId;
            return Task.FromResult(Meta);
        }

        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query)
        {
            SearchCalls++;
            return Task.FromResult(Search);
        }

        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotImplementedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotImplementedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotImplementedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotImplementedException();
    }
}
