using System.IO;
using System.Windows;
using ROROROblox.App.Games;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.StreamerMode;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>What a Library render fixture holds.</summary>
internal enum LibraryScene
{
    /// <summary>The C1 mock's showcase: a default favourite, a private server, plain favourites, a never-played one, two Recent places.</summary>
    Showcase,

    /// <summary>One hundred favourites: the virtualisation bar (spec 6).</summary>
    Hundred,

    /// <summary>Nothing saved, nothing played: the invitation.</summary>
    Empty,

    /// <summary>Names far longer than a tile: the ellipsis case.</summary>
    LongNames,
}

/// <summary>
/// Builds the real <c>GamesPage</c> over in-memory stores for the item-9 render gates and the opt-in
/// captures. Every store completes synchronously, so the page's async load finishes inside the build call and
/// nothing in a render test waits on I/O. Every game, server and account name is made up.
/// </summary>
internal static class LibraryPageFixtures
{
    public static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    /// <summary>The four themes the item's captures and gates cover: the default, the dark alternate, the one that merges cyan into magenta, and flatline (colour removed).</summary>
    public static IReadOnlyList<Theme> Themes()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-library-themes-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn && t.Id is "brand" or "midnight" or "magenta-heat" or "flatline").ToList();
        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch (IOException) { }
        Assert.Equal(4, themes.Count);
        return themes;
    }

    public sealed record Built(Window Window, GamesPage Page, LibraryViewModel ViewModel, IReadOnlyList<AccountSummary> Accounts);

    /// <summary>
    /// Hosts the page at the shell's content size (the 920x640 shell less its rail and margins) and loads it
    /// BEFORE the harness arranges anything, so the one layout pass the harness runs is the finite one: a
    /// post-Loaded infinite measure would realise every item and say nothing about virtualisation.
    /// </summary>
    public static Built Build(
        LibraryScene scene = LibraryScene.Showcase,
        string view = "grid",
        string filters = "",
        double width = 684,
        double height = 540,
        bool streamerMode = false)
    {
        var favourites = new MemFavorites();
        var servers = new MemServers();
        var stats = new Dictionary<long, GameStat>();
        Seed(scene, favourites, servers, stats);

        var settings = new MainViewModelTests.FakeAppSettings { LibraryView = view, LibraryFilters = filters };
        var accounts = RosterFor(streamerMode);
        var catalog = new LibraryCatalog(favourites, servers, new MemStats(stats), new MemHistory(), new NoIcons());
        var vm = new LibraryViewModel(
            catalog, favourites, servers, new NullApi(), settings,
            _ => Task.FromResult<LaunchTarget?>(null),
            () => accounts,
            (_, _) => Task.CompletedTask);

        // Every await inside LoadAsync is on an already-completed task, so this returns synchronously.
        vm.LoadAsync().GetAwaiter().GetResult();

        var page = new GamesPage(vm, accounts);
        var window = ThemedWindowRender.HostPage(page, width, height);
        return new Built(window, page, vm, accounts);
    }

    /// <summary>
    /// The Play as picker alone. A <c>ContextMenu</c> cannot have a parent, so it is not hosted in the window: it is given the
    /// theme's resources directly, measured and arranged detached, and the window shows it through a VisualBrush. Same
    /// template, same item template, same bindings as an opened menu; what an opened one adds is a popup HWND this harness
    /// cannot render. The returned menu is the live one, for tests that read its items.
    /// </summary>
    public static (Window Window, AccountPickerFlyout Menu) BuildFlyout(Theme theme, bool streamerMode = false)
    {
        var menu = new AccountPickerFlyout { ItemsSource = RosterFor(streamerMode), Width = 280 };
        menu.Resources.MergedDictionaries.Add(ThemedRender.Resources(theme));
        menu.ApplyTemplate();
        menu.Measure(new Size(280, double.PositiveInfinity));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();

        var stage = new System.Windows.Shapes.Rectangle
        {
            Width = 316,
            Height = menu.DesiredSize.Height + 32,
            Margin = new Thickness(0),
            Fill = new System.Windows.Media.VisualBrush(menu)
            {
                Stretch = System.Windows.Media.Stretch.None,
                AlignmentX = System.Windows.Media.AlignmentX.Left,
                AlignmentY = System.Windows.Media.AlignmentY.Top,
                Transform = new System.Windows.Media.TranslateTransform(16, 16),
            },
        };
        var window = new Window { Content = stage, Width = stage.Width, Height = stage.Height };
        window.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "BgBrush");
        return (window, menu);
    }

    /// <summary>A roster of five: main (in a game), a follow-marked alt, a ready alt, a limited one and an expired one (the D2 mock's).</summary>
    public static IReadOnlyList<AccountSummary> RosterFor(bool streamerMode)
    {
        AccountSummary Row(string name, bool main = false) =>
            new(new Account(Guid.NewGuid(), name, "", Now.AddDays(-30), null, IsMain: main, RobloxUserId: 1000 + name.Length));

        var main = Row("NovaFake", main: true);
        main.PresenceState = UserPresenceType.InGame;
        main.CurrentGameName = "Petal Simulator";
        var follower = Row("PetFake07");
        follower.JoinViaFriend = true;
        var ready = Row("AltFake");
        var limited = Row("BuildFake");
        limited.SessionLimited = true;
        var expired = Row("OldFake2024");
        expired.SessionExpired = true;

        var roster = new List<AccountSummary> { main, follower, ready, limited, expired };
        if (streamerMode)
        {
            var identity = new MaskedIdentity();
            foreach (var row in roster) row.AttachIdentityProvider(identity);
        }

        return roster;
    }

    private static void Seed(LibraryScene scene, MemFavorites favourites, MemServers servers, Dictionary<long, GameStat> stats)
    {
        FavoriteGame Fav(long place, string name, bool isDefault = false, string? local = null) =>
            new(place, place * 10, name, "", isDefault, Now.AddDays(-20), local);

        switch (scene)
        {
            case LibraryScene.Empty:
                break;

            case LibraryScene.Hundred:
                for (var i = 1; i <= 100; i++)
                {
                    favourites.Items.Add(Fav(1000 + i, i % 2 == 0 ? $"Obby World {i}" : $"Tycoon Town {i}", isDefault: i == 1));
                }

                stats[1001] = new GameStat("Tycoon Town 1", 4, TimeSpan.FromHours(7), Now.AddHours(-3));
                break;

            case LibraryScene.LongNames:
                favourites.Items.Add(Fav(7001, "The Extraordinarily Long Named Simulator of Many Worlds and Wonders", isDefault: true));
                servers.Items.Add(new SavedPrivateServer(Guid.NewGuid(), 7002, "FAKE-LINK-CODE", PrivateServerCodeKind.LinkCode,
                    "The Clan Base That Never Stops Having A Very Long Name", "Another Rather Long Place Name", "", Now.AddDays(-3), Now.AddHours(-5)));
                stats[7001] = new GameStat("The Extraordinarily Long Named Simulator of Many Worlds and Wonders", 12, TimeSpan.FromHours(61), Now.AddHours(-2));
                break;

            default:
                favourites.Items.Add(Fav(2001, "Petal Simulator", isDefault: true));
                servers.Items.Add(new SavedPrivateServer(Guid.NewGuid(), 2001, "FAKE-LINK-CODE", PrivateServerCodeKind.LinkCode,
                    "Clan Base", "Petal Simulator", "", Now.AddDays(-9), Now.AddHours(-5)));
                favourites.Items.Add(Fav(2002, "Sprout Garden"));
                favourites.Items.Add(Fav(2003, "Adopt Pals"));
                stats[2001] = new GameStat("Petal Simulator", 30, TimeSpan.FromHours(61), Now.AddHours(-2));
                stats[2002] = new GameStat("Sprout Garden", 5, TimeSpan.FromHours(3), Now.AddDays(-1));
                // Recent: played from RoRoRo, never saved.
                stats[2004] = new GameStat("Fruit Blox", 2, TimeSpan.FromMinutes(40), Now.AddDays(-4));
                stats[2005] = new GameStat("Mystery Manor", 1, TimeSpan.FromMinutes(12), Now.AddDays(-6));
                break;
        }
    }

    // ---- in-memory stores: Task.FromResult everywhere, so awaiting them never leaves the calling thread ----

    internal sealed class MemFavorites : IFavoriteGameStore
    {
        public List<FavoriteGame> Items { get; } = [];
        public event EventHandler? DefaultChanged { add { } remove { } }
        public Task<IReadOnlyList<FavoriteGame>> ListAsync() => Task.FromResult<IReadOnlyList<FavoriteGame>>(Items.ToList());
        public Task<FavoriteGame?> GetDefaultAsync() => Task.FromResult(Items.FirstOrDefault(i => i.IsDefault));
        public Task<FavoriteGame> AddAsync(long placeId, long universeId, string name, string thumbnailUrl)
        {
            var g = new FavoriteGame(placeId, universeId, name, thumbnailUrl, false, DateTimeOffset.UtcNow);
            Items.Add(g);
            return Task.FromResult(g);
        }

        public Task RemoveAsync(long placeId) { Items.RemoveAll(i => i.PlaceId == placeId); return Task.CompletedTask; }
        public Task SetDefaultAsync(long placeId)
        {
            for (var i = 0; i < Items.Count; i++) Items[i] = Items[i] with { IsDefault = Items[i].PlaceId == placeId };
            return Task.CompletedTask;
        }

        public Task ClearDefaultAsync()
        {
            for (var i = 0; i < Items.Count; i++) Items[i] = Items[i] with { IsDefault = false };
            return Task.CompletedTask;
        }

        public Task UpdateLocalNameAsync(long placeId, string? localName)
        {
            var i = Items.FindIndex(g => g.PlaceId == placeId);
            if (i < 0) throw new KeyNotFoundException();
            Items[i] = Items[i] with { LocalName = string.IsNullOrWhiteSpace(localName) ? null : localName };
            return Task.CompletedTask;
        }
    }

    internal sealed class MemServers : IPrivateServerStore
    {
        public List<SavedPrivateServer> Items { get; } = [];
        public event EventHandler? DefaultChanged { add { } remove { } }
        public Task<IReadOnlyList<SavedPrivateServer>> ListAsync() => Task.FromResult<IReadOnlyList<SavedPrivateServer>>(Items.ToList());
        public Task<SavedPrivateServer?> GetAsync(Guid id) => Task.FromResult(Items.FirstOrDefault(s => s.Id == id));
        public Task<SavedPrivateServer> AddAsync(long placeId, string code, PrivateServerCodeKind codeKind, string name, string placeName, string thumbnailUrl)
        {
            var s = new SavedPrivateServer(Guid.NewGuid(), placeId, code, codeKind, name, placeName, thumbnailUrl, DateTimeOffset.UtcNow, null);
            Items.Add(s);
            return Task.FromResult(s);
        }

        public Task RemoveAsync(Guid id) { Items.RemoveAll(s => s.Id == id); return Task.CompletedTask; }
        public Task TouchLastLaunchedAsync(Guid id) => Task.CompletedTask;
        public Task UpdateLocalNameAsync(Guid serverId, string? localName)
        {
            var i = Items.FindIndex(s => s.Id == serverId);
            if (i < 0) throw new KeyNotFoundException();
            Items[i] = Items[i] with { LocalName = string.IsNullOrWhiteSpace(localName) ? null : localName };
            return Task.CompletedTask;
        }

        public Task SetDefaultAsync(Guid id)
        {
            for (var i = 0; i < Items.Count; i++) Items[i] = Items[i] with { IsDefault = Items[i].Id == id };
            return Task.CompletedTask;
        }

        public Task ClearDefaultAsync()
        {
            for (var i = 0; i < Items.Count; i++) Items[i] = Items[i] with { IsDefault = false };
            return Task.CompletedTask;
        }
    }

    internal sealed class MemStats(Dictionary<long, GameStat> games) : ISessionStatsStore
    {
        public Task ApplyAsync(StatsEvent statsEvent) => Task.CompletedTask;
        public Task<SessionStats> ReadAsync() => Task.FromResult(SessionStats.Empty with { Games = games });
        public Task ClearAsync() => Task.CompletedTask;
    }

    internal sealed class MemHistory : ISessionHistoryStore
    {
        public Task<IReadOnlyList<LaunchSession>> ListAsync() => Task.FromResult<IReadOnlyList<LaunchSession>>([]);
        public Task AddAsync(LaunchSession session) => Task.CompletedTask;
        public Task MarkEndedAsync(Guid sessionId, DateTimeOffset endedAtUtc, string? outcomeHint = null) => Task.CompletedTask;
        public Task MarkOutcomeAsync(Guid sessionId, string outcomeHint) => Task.CompletedTask;
        public Task UpdateGameAsync(Guid id, string? gameName, long? placeId, Guid? privateServerId) => Task.CompletedTask;
        public Task ClearAsync() => Task.CompletedTask;
    }

    internal sealed class NoIcons : IGameIconCache
    {
        public event EventHandler<GameIconResolvedEventArgs>? IconResolved { add { } remove { } }
        public GameIconInfo? TryGet(long placeId) => null;
        public bool Request(long placeId) => false;
        public Task<bool> ResolveAsync(long placeId, CancellationToken ct = default) => Task.FromResult(false);
        public void AttachQueue(Func<long, bool> enqueue) { }
    }

    internal sealed class NullApi : IRobloxApi
    {
        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId) => Task.FromResult<GameMetadata?>(null);
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => Task.FromResult<IReadOnlyList<GameSearchResult>>([]);
        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => throw new NotImplementedException();
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotImplementedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotImplementedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotImplementedException();
    }

    /// <summary>An always-active streamer provider: every account becomes "StreamerFake" with a pack:// avatar, whatever its real name.</summary>
    internal sealed class MaskedIdentity : IStreamerIdentityProvider
    {
        public const string FakeName = "StreamerFake";
        public const string FakeAvatar = "pack://application:,,,/Assets/streamer-fake.png";
        public bool IsActive => true;
        public Task InitializeAsync(IReadOnlyCollection<(Guid accountId, StreamerIdentity identity)> accountIdentities) => Task.CompletedTask;
        public Task SetActiveAsync(bool active) => Task.CompletedTask;
        public DisplayIdentity ForAccount(Guid accountId, string realName, string realAvatarUrl) => new(FakeName, FakeAvatar);
        public DisplayIdentity ForFriend(long robloxUserId, string realName, string realAvatarUrl) => new(FakeName, FakeAvatar);
        public Task RerollAsync(string identityKey) => Task.CompletedTask;
        public Task RerollAllAsync() => Task.CompletedTask;
        public event EventHandler? Changed { add { } remove { } }
    }
}
