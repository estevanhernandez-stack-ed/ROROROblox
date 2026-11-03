using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.Games;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Marketing frames of the v1.36 UI, rendered from the test harness instead of the live app (streamer mode on the
/// live app masks the new account facts). The REAL <c>MainWindow</c> and <c>GamesPage</c> are built over fake data:
/// every account, game, server, number and avatar here is made up or a public game name. Nothing is fetched.
/// <para>
/// Per culture (en, de, es, fr, pl, pt-BR, ru) under the brand theme at 96 DPI, four frames: <c>accounts-panel</c>,
/// <c>accounts-health</c>, <c>library-grid</c>, <c>library-list</c>. Each writes the raw window PNG
/// (<c>docs/videos/v136-shots/&lt;lang&gt;/</c>, what the trailer renderer reads) and the 1920x1080 store composite
/// (<c>docs/store/screenshots-v136/&lt;lang&gt;/</c>), the composite doing exactly what <c>New-StoreFrame</c> in
/// <c>scripts/capture-ui.ps1</c> does: navy #0F1F31 ground, window unscaled and centred, a three-rectangle shadow.
/// </para>
/// <para>
/// Opt-in: set <c>RORORO_MARKETING_FRAMES=1</c>. Facts and status are in the data at build time, so nothing waits on a
/// timer. No SetRootDpi on the shared render thread (AccountRowCollapsedRenderTests forbids it).
/// </para>
/// </summary>
[Collection("MutatesUiCulture")]
public class MarketingFrameTests
{
    private static readonly string[] Cultures = ["en", "de", "es", "fr", "pl", "pt-BR", "ru"];

    /// <summary>
    /// The window the accounts frames are laid out at. The declared startup size (900x600) shows two rows and nothing
    /// of a second one beside an open panel; the live store frames are drawn from a window about this size too.
    /// </summary>
    private const double AccountsWidth = 1180;
    private const double AccountsHeight = 840;

    /// <summary>
    /// A row is about 172 DIP tall (name, status, Follow chips, the add-tag button, the facts line), so five rows plus the
    /// header would need about 1120, past the 1080 canvas. The health frame shows the four states that matter in
    /// a window tall enough for all four whole.
    /// </summary>
    private const double HealthHeight = 890;

    private const int CanvasWidth = 1920;
    private const int CanvasHeight = 1080;

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public MarketingFrameTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_MARKETING_FRAMES") != "1")
            {
                Skip = "Opt-in capture: set RORORO_MARKETING_FRAMES=1 to write docs/videos/v136-shots and docs/store/screenshots-v136.";
            }
        }
    }

    /// <summary>The Library page hosted at the shell's content size, a little roomier so twelve entries show.</summary>
    private const double LibraryWidth = 920;
    private const double LibraryGridHeight = 640;
    private const double LibraryListHeight = 770;

    private sealed record Frame(string Name, Func<Window> Build, double Width, double Height);

    private static readonly Frame[] Frames =
    [
        new("accounts-panel", () => BuildAccounts(health: false), AccountsWidth, AccountsHeight),
        new("accounts-health", () => BuildAccounts(health: true), AccountsWidth, HealthHeight),
        new("library-grid", () => BuildLibrary("grid", LibraryGridHeight), LibraryWidth, LibraryGridHeight),
        new("library-list", () => BuildLibrary("list", LibraryListHeight), LibraryWidth, LibraryListHeight),
    ];

    /// <summary>
    /// The harness arranges at the content's DESIRED size, which can come up narrower than the window and clip the
    /// right edge of the toolbar and the row buttons. Re-lay the content out at exactly the window size, and raise
    /// Loaded on the per-row FPS combos (the window never shows, so nothing else would) so each reads its cap.
    /// </summary>
    private static void Settle(FrameworkElement content, double width, double height)
    {
        void Layout()
        {
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            WindowRenderHost.DrainQueue();
        }

        Layout();
        var combos = new List<System.Windows.Controls.ComboBox>();
        void Walk(DependencyObject node)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is System.Windows.Controls.ComboBox cb
                    && cb.Items.OfType<System.Windows.Controls.ComboBoxItem>().Any(it => "60".Equals(it.Tag?.ToString())))
                {
                    combos.Add(cb);
                }

                Walk(child);
            }
        }

        Walk(content);
        foreach (var cb in combos) cb.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, cb));
        Layout();
    }

    private static Theme Brand() => LibraryPageFixtures.Themes().Single(t => t.Id == "brand");

    [OptInFact]
    public void WriteTheV136MarketingFrames()
    {
        var root = RepoRoot();
        var brand = Brand();
        var previous = TranslationSource.Instance.CurrentCulture;
        var written = 0;
        try
        {
            foreach (var culture in Cultures)
            {
                TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
                foreach (var frame in Frames)
                {
                    var (raw, store, size) = ThemedWindowRender.Inspect(brand, $"marketing [{frame.Name}/{culture}]", frame.Build, content =>
                    {
                        var ground = content.TryFindResource("BgBrush") as Brush
                            ?? throw new InvalidOperationException("BgBrush did not resolve; refusing to paint a white PNG.");
                        Settle(content, frame.Width, frame.Height);
                        var w = (int)Math.Round(content.ActualWidth);
                        var h = (int)Math.Round(content.ActualHeight);
                        if (w != (int)frame.Width || h != (int)frame.Height)
                        {
                            throw new InvalidOperationException($"[{frame.Name}/{culture}] laid out at {w}x{h}, not {frame.Width}x{frame.Height}.");
                        }

                        var rawPng = RenderWindow(content, ground, w, h);
                        var storePng = Composite(rawPng, w, h, frame.Name, culture);
                        return (rawPng, storePng, (w, h));
                    });

                    var rawPath = Path.Combine(root, "docs", "videos", "v136-shots", culture, frame.Name + ".png");
                    var storePath = Path.Combine(root, "docs", "store", "screenshots-v136", culture, frame.Name + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(rawPath)!);
                    Directory.CreateDirectory(Path.GetDirectoryName(storePath)!);
                    File.WriteAllBytes(rawPath, raw);
                    File.WriteAllBytes(storePath, store);
                    written += 2;
                    _output.WriteLine($"{culture}/{frame.Name}: window {size.Item1}x{size.Item2}");
                }
            }
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }

        Assert.Equal(Cultures.Length * Frames.Length * 2, written);
    }

    // ---- rendering --------------------------------------------------------------------------------------------

    /// <summary>The window content on the theme's own ground, cropped to the window (no offset to get wrong: see ThemedWindowRender.RenderRegion).</summary>
    private static byte[] RenderWindow(FrameworkElement content, Brush ground, int w, int h)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(ground, null, new Rect(0, 0, w, h));
            dc.DrawRectangle(
                new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }

        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return Encode(bitmap);
    }

    /// <summary>
    /// New-StoreFrame, in WPF: navy canvas, the window unscaled at x = (W - w) / 2 and y = (H - h) * 0.42 (rounded half
    /// to even, as PowerShell's [int] does), under three rectangles of alpha 18*i, padded i*6, offset +4 in y, for i = 3..1.
    /// A window larger than the canvas fails: a screenshot is never scaled.
    /// </summary>
    private static byte[] Composite(byte[] windowPng, int w, int h, string frame, string culture)
    {
        if (w > CanvasWidth || h > CanvasHeight)
        {
            throw new InvalidOperationException(
                $"[{frame}/{culture}] the window is {w}x{h}, larger than the {CanvasWidth}x{CanvasHeight} canvas. Resize the window; upscaling a screenshot is not an option here.");
        }

        var decoder = BitmapDecoder.Create(new MemoryStream(windowPng), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];
        var x = (int)Math.Round((CanvasWidth - w) / 2.0, MidpointRounding.ToEven);
        var y = (int)Math.Round((CanvasHeight - h) * 0.42, MidpointRounding.ToEven);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x0F, 0x1F, 0x31)), null, new Rect(0, 0, CanvasWidth, CanvasHeight));
            for (var i = 3; i >= 1; i--)
            {
                var pad = i * 6;
                dc.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb((byte)(18 * i), 0, 0, 0)), null,
                    new Rect(x - pad, y - pad + 4, w + pad * 2, h + pad * 2));
            }

            dc.DrawImage(image, new Rect(x, y, w, h));
        }

        var bitmap = new RenderTargetBitmap(CanvasWidth, CanvasHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        return Encode(bitmap);
    }

    private static byte[] Encode(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir!;
    }

    // ---- fake avatars -----------------------------------------------------------------------------------------

    private static readonly string AvatarDir = Path.Combine(Path.GetTempPath(), "rororo-marketing-avatars-" + Guid.NewGuid().ToString("N"));
    private static readonly HashSet<string> Written = [];

    /// <summary>
    /// A procedural avatar: a diagonal gradient disc in two brand colours with a white initial, written to a temp PNG and
    /// returned as a file URI (what the row's <c>Image Source</c> binding takes). Drawn square so the row's own rounding
    /// does the circle; the disc fills the frame.
    /// </summary>
    private static string Avatar(string name, Color a, Color b)
    {
        Directory.CreateDirectory(AvatarDir);
        var path = Path.Combine(AvatarDir, $"{name}-{a.R:X2}{a.G:X2}{a.B:X2}.png");
        // WPF holds the file open once an Image has loaded it, so each avatar is written once per process.
        if (File.Exists(path) && Written.Contains(path)) return new Uri(path).AbsoluteUri;
        Written.Add(path);
        const int size = 128;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var fill = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
            dc.DrawEllipse(fill, null, new Point(size / 2.0, size / 2.0), size / 2.0, size / 2.0);
            var text = new FormattedText(
                name[..1].ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                72, Brushes.White, 1.0);
            dc.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        File.WriteAllBytes(path, Encode(bitmap));
        return new Uri(path).AbsoluteUri;
    }

    private static readonly Color Cyan = Color.FromRgb(0x17, 0xD4, 0xFA);
    private static readonly Color Magenta = Color.FromRgb(0xF2, 0x2F, 0x89);
    private static readonly Color Navy = Color.FromRgb(0x0F, 0x1F, 0x31);
    private static readonly Color Amber = Color.FromRgb(0xF5, 0xB5, 0x2B);
    private static readonly Color Violet = Color.FromRgb(0x7C, 0x4D, 0xFF);

    // ---- the accounts frames ----------------------------------------------------------------------------------

    private static readonly Guid ClanBase = Guid.Parse("00000000-0000-0000-0000-0000000000c1");
    private static readonly Guid TradeRoom = Guid.Parse("00000000-0000-0000-0000-0000000000c2");

    private static LaunchSession Session(Guid account, string game, long place, TimeSpan ago, int minutes, Guid? server = null) =>
        new(Guid.NewGuid(), account, "Alt", null, game, place, server is not null,
            DateTimeOffset.UtcNow - ago, DateTimeOffset.UtcNow - ago + TimeSpan.FromMinutes(minutes), null, server);

    private static Window BuildAccounts(bool health)
    {
        var now = DateTimeOffset.UtcNow;
        var store = new LibraryPageFixtures.MemFavorites();
        store.Items.Add(new FavoriteGame(8737899170, 1, "Pet Simulator 99", "", IsDefault: true, AddedAt: now));
        store.Items.Add(new FavoriteGame(920587237, 2, "Adopt Me!", "", IsDefault: false, AddedAt: now));
        store.Items.Add(new FavoriteGame(126884695634066, 3, "Grow a Garden", "", IsDefault: false, AddedAt: now));
        store.Items.Add(new FavoriteGame(2753915549, 4, "Blox Fruits", "", IsDefault: false, AddedAt: now));
        var built = MainViewModelTests.Build(favorites: store);

        // The saved games land in both pickers (the header widget and each row's) through the real reload path.
        built.Vm.ReloadGamesAsync().GetAwaiter().GetResult();
        var pet = built.Vm.AvailableGames.First(g => g.PlaceId == 8737899170);
        var adopt = built.Vm.AvailableGames.First(g => g.PlaceId == 920587237);

        // name, avatar colours, robux, membership, friends online, note, last played ago
        var people = new (string Name, Color A, Color B, long Robux, string? Plus, int Friends, string? Note, TimeSpan Ago, string LastGame)[]
        {
            ("NovaBuilds", Cyan, Violet, 12480, "plus", 7, "Clan bank. Fridays.", TimeSpan.FromMinutes(42), "Pet Simulator 99"),
            ("PetGrinder07", Magenta, Amber, 3105, null, 9, null, TimeSpan.FromMinutes(18), "Adopt Me!"),
            ("AltThree", Violet, Cyan, 860, "plus", 2, "Egg hatcher, check at noon.", TimeSpan.FromHours(3), "Grow a Garden"),
            ("GardenKeeper", Amber, Magenta, 5210, null, 4, null, TimeSpan.FromHours(7), "Grow a Garden"),
            ("BloxRunner", Cyan, Magenta, 1790, null, 6, null, TimeSpan.FromDays(1), "Blox Fruits"),
        };

        // The health frame drops the fifth (healthy) account: see HealthHeight.
        for (var i = 0; i < (health ? 4 : people.Length); i++)
        {
            var p = people[i];
            var row = new AccountSummary(new Account(
                Id: Guid.NewGuid(), DisplayName: p.Name, AvatarUrl: Avatar(p.Name, p.A, p.B),
                CreatedAt: now.AddDays(-60), LastLaunchedAt: now - p.Ago, IsMain: i == 0,
                IsSelected: true,
                Facts: new AccountFacts(p.Robux, p.Plus, p.Friends, now.AddMinutes(-3), null),
                Note: p.Note));
            var id = row.Id;
            var expanded = !health && i == 0;
            var panel = new AccountPanelViewModel(
                row,
                () => Task.FromResult<IReadOnlyList<LaunchSession>>(expanded ? NovaHistory(id) : []),
                () => Task.FromResult<IReadOnlyList<LibraryEntry>>(expanded ? NovaLibrary() : []),
                _ => Task.CompletedTask,
                () => Task.CompletedTask,
                openLibrary: () => { });
            row.AttachPanel(panel);
            panel.PrimeLastPlayed([Session(id, p.LastGame, 1L, p.Ago, 30)]);
            row.SelectedGame = i == 1 ? adopt : pet;
            row.FpsCap = 60;
            built.Vm.Accounts.Add(row);

            if (!health && i < 2)
            {
                // In a game, the way presence makes a row read: the dot, "In <game> · <age>" and the Stop / Recycle
                // buttons come from IsRunning + presence, all set here so nothing waits on a poll.
                row.IsRunning = true;
                row.RunningSinceUtc = now - p.Ago;
                row.PresenceState = Core.UserPresenceType.InGame;
                row.CurrentGameName = i == 0 ? "Pet Simulator 99" : "Adopt Me!";
                row.CurrentPlaceId = i == 0 ? 8737899170 : 920587237;
                row.InGameSinceUtc = now - p.Ago;
            }
        }

        if (health)
        {
            built.Vm.Accounts[1].SessionNeedsVerification = true;
            built.Vm.Accounts[2].SessionExpired = true;
            built.Vm.Accounts[3].SessionLimited = true;
            built.Vm.Accounts[3].JoinViaFriend = true;
        }
        else
        {
            built.Vm.Accounts[0].Panel!.IsExpanded = true;
        }

        // The footer reads the view model's fields at construction: multi-instance on, and a client count that agrees with
        // the rows shown in a game. There is no public setter (a live count comes from the process tracker), so the
        // fields are set directly, before the window reads them.
        void Set(string field, object value) =>
            typeof(MainViewModel).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(built.Vm, value);
        Set("_multiInstanceState", MultiInstanceState.On);
        if (!health)
        {
            Set("_liveProcessCount", 2);
            Set("_aggregateClientBytes", 5_400_000_000L);
        }

        var window = new ROROROblox.App.MainWindow(built.Vm) { Width = AccountsWidth, Height = health ? HealthHeight : AccountsHeight };
        return window;
    }

    private static IReadOnlyList<LaunchSession> NovaHistory(Guid id) =>
    [
        Session(id, "Pet Simulator 99", 8737899170, TimeSpan.FromMinutes(42), 14 * 60),
        Session(id, "Pet Simulator 99", 8737899170, TimeSpan.FromHours(5), 25, ClanBase),
        Session(id, "Grow a Garden", 126884695634066, TimeSpan.FromDays(1), 180),
    ];

    private static IReadOnlyList<LibraryEntry> NovaLibrary() =>
    [
        new(LibraryEntryKind.Favourite, 8737899170, null, "Pet Simulator 99", null, null, true, null, null, new LaunchTarget.Place(8737899170)),
        new(LibraryEntryKind.Favourite, 920587237, null, "Adopt Me!", null, null, false, null, null, new LaunchTarget.Place(920587237)),
        new(LibraryEntryKind.Favourite, 2753915549, null, "Blox Fruits", null, null, false, null, null, new LaunchTarget.Place(2753915549)),
        new(LibraryEntryKind.PrivateServer, 8737899170, ClanBase, "Clan base", null, null, false, null, null,
            new LaunchTarget.PrivateServer(8737899170, "FAKE-CODE", PrivateServerCodeKind.AccessCode)),
        new(LibraryEntryKind.PrivateServer, 920587237, TradeRoom, "Trade room", null, null, false, null, null,
            new LaunchTarget.PrivateServer(920587237, "FAKE-CODE", PrivateServerCodeKind.AccessCode)),
    ];

    // ---- the Library frames -----------------------------------------------------------------------------------

    private static Window BuildLibrary(string view, double height)
    {
        var now = DateTimeOffset.UtcNow;
        var favourites = new LibraryPageFixtures.MemFavorites();
        var servers = new LibraryPageFixtures.MemServers();
        var stats = new Dictionary<long, GameStat>();

        FavoriteGame Fav(long place, string name, bool isDefault = false) =>
            new(place, place * 10, name, "", isDefault, now.AddDays(-20));
        SavedPrivateServer Server(long place, string name, string placeName, TimeSpan ago) =>
            new(Guid.NewGuid(), place, "FAKE-LINK-CODE", PrivateServerCodeKind.LinkCode, name, placeName, "", now.AddDays(-9), now - ago);

        favourites.Items.Add(Fav(8737899170, "Pet Simulator 99", isDefault: true));
        favourites.Items.Add(Fav(920587237, "Adopt Me!"));
        favourites.Items.Add(Fav(126884695634066, "Grow a Garden"));
        favourites.Items.Add(Fav(2753915549, "Blox Fruits"));
        favourites.Items.Add(Fav(4924922222, "Brookhaven RP"));
        favourites.Items.Add(Fav(142823291, "Murder Mystery 2"));
        servers.Items.Add(Server(8737899170, "Clan base", "Pet Simulator 99", TimeSpan.FromHours(5)));
        servers.Items.Add(Server(920587237, "Trade room", "Adopt Me!", TimeSpan.FromDays(2)));
        servers.Items.Add(Server(126884695634066, "Farm lobby", "Grow a Garden", TimeSpan.FromDays(4)));

        stats[8737899170] = new GameStat("Pet Simulator 99", 84, TimeSpan.FromHours(212), now.AddHours(-2));
        stats[920587237] = new GameStat("Adopt Me!", 31, TimeSpan.FromHours(57), now.AddHours(-9));
        stats[126884695634066] = new GameStat("Grow a Garden", 22, TimeSpan.FromHours(40), now.AddDays(-1));
        stats[2753915549] = new GameStat("Blox Fruits", 12, TimeSpan.FromHours(18), now.AddDays(-3));
        stats[4924922222] = new GameStat("Brookhaven RP", 4, TimeSpan.FromHours(5), now.AddDays(-6));
        // Recent: played from RoRoRo, never saved.
        stats[6516141723] = new GameStat("Doors", 6, TimeSpan.FromHours(4), now.AddDays(-2));
        stats[1962086868] = new GameStat("Tower of Hell", 3, TimeSpan.FromMinutes(70), now.AddDays(-5));
        stats[286090429] = new GameStat("Arsenal", 2, TimeSpan.FromMinutes(35), now.AddDays(-6));

        var settings = new MainViewModelTests.FakeAppSettings { LibraryView = view, LibraryFilters = "" };
        var accounts = Roster();
        var catalog = new LibraryCatalog(favourites, servers, new LibraryPageFixtures.MemStats(stats), new LibraryPageFixtures.MemHistory(), new ProceduralIcons());
        var vm = new LibraryViewModel(
            catalog, favourites, servers, new LibraryPageFixtures.NullApi(), settings,
            _ => Task.FromResult<LaunchTarget?>(null),
            () => accounts,
            (_, _) => Task.CompletedTask);
        vm.LoadAsync().GetAwaiter().GetResult();

        var page = new GamesPage(vm, accounts);
        return ThemedWindowRender.HostPage(page, LibraryWidth, height);
    }

    /// <summary>
    /// Game icons made in code: a diagonal gradient in two brand colours with a few translucent shapes, one file per place,
    /// handed to the catalog as the "cached" icon. Abstract on purpose (no real thumbnail, no game's artwork, no text),
    /// and nothing is fetched: <see cref="Request"/> and <see cref="ResolveAsync"/> do nothing.
    /// </summary>
    private sealed class ProceduralIcons : IGameIconCache
    {
        private static readonly (Color A, Color B)[] Palette =
        [
            (Cyan, Violet), (Magenta, Amber), (Color.FromRgb(0x2B, 0xD9, 0xA0), Cyan),
            (Amber, Magenta), (Violet, Magenta), (Color.FromRgb(0xFF, 0x6B, 0x57), Amber), (Cyan, Navy),
        ];

        private readonly Dictionary<long, GameIconInfo> _made = [];

        public event EventHandler<GameIconResolvedEventArgs>? IconResolved { add { } remove { } }

        public GameIconInfo? TryGet(long placeId)
        {
            if (_made.TryGetValue(placeId, out var known)) return known;
            Directory.CreateDirectory(AvatarDir);
            var path = Path.Combine(AvatarDir, $"game-{placeId}.png");
            if (Written.Add(path))
            {
                var seed = (int)(placeId % 997);
                var colours = _made.Count;
                var (a, b) = Palette[colours % Palette.Length];
                const int size = 256;
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1)), null, new Rect(0, 0, size, size));
                    var soft = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
                    var dark = new SolidColorBrush(Color.FromArgb(70, 15, 31, 49));
                    dc.DrawEllipse(soft, null, new Point(40 + seed % 90, 60 + seed % 50), 70 + seed % 30, 70 + seed % 30);
                    dc.DrawEllipse(dark, null, new Point(200 - seed % 60, 210 - seed % 40), 90, 90);
                    var tri = new StreamGeometry();
                    using (var g = tri.Open())
                    {
                        g.BeginFigure(new Point(150 + seed % 40, 40), true, true);
                        g.LineTo(new Point(236, 150 + seed % 30), true, false);
                        g.LineTo(new Point(120, 130), true, false);
                    }

                    dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), null, tri);
                }

                var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                File.WriteAllBytes(path, Encode(bitmap));
            }

            var info = new GameIconInfo("", 0, new Uri(path).AbsoluteUri, DateTimeOffset.UtcNow);
            _made[placeId] = info;
            return info;
        }

        public bool Request(long placeId) => false;
        public Task<bool> ResolveAsync(long placeId, CancellationToken ct = default) => Task.FromResult(false);
        public void AttachQueue(Func<long, bool> enqueue) { }
    }

    private static IReadOnlyList<AccountSummary> Roster()
    {
        var now = DateTimeOffset.UtcNow;
        return new[] { "NovaBuilds", "PetGrinder07", "AltThree", "GardenKeeper", "BloxRunner" }
            .Select((n, i) => new AccountSummary(new Account(Guid.NewGuid(), n, "", now.AddDays(-30), null, IsMain: i == 0, RobloxUserId: 1000 + i)))
            .ToList();
    }
}
