using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests.Rendering;

/// <summary>What an account's panel holds in a render fixture.</summary>
internal enum PanelContent
{
    /// <summary>Three Recent entries (a saved game, a saved private server, an unsaved game), two Favourites (one default) and one private server: a light panel, every column under its cap of three.</summary>
    Full,

    /// <summary>
    /// The heaviest panel the caps allow for: eight Recent sessions (eight distinct places, one of them a private server
    /// group), six Favourites and four saved private servers. The panel shows three of each (the C2 ruling P1), counts the
    /// totals in the Favourites and Private servers headers and offers See all on both.
    /// </summary>
    Heavy,

    /// <summary>Nothing anywhere: all three one-line empty states read.</summary>
    Empty,

    /// <summary>One Recent entry whose private server was never saved: shown, disabled, no Save.</summary>
    UnsavedPrivate,

    /// <summary>Names far longer than a column: the ellipsis case, in every group, beside a DEFAULT or PRIVATE tag and beside Save.</summary>
    LongNames,
}

/// <summary>
/// Builds the real <c>MainWindow</c> with N fake accounts, the first one carrying a populated panel, for the
/// item-7 render gates and the opt-in captures. Every account, game and server name is made up. The panel's
/// delegates are completed tasks, so opening it fills the three groups inline; no store, no network.
/// </summary>
internal static class PanelWindowFixtures
{
    public static Window Build(
        int accounts = 1,
        bool expanded = true,
        PanelContent content = PanelContent.Full,
        string? note = "Farm alt, keep at 20 FPS. Clan bank on Fridays.",
        string firstName = "NovaFake",
        bool savedGames = false,
        Func<AccountSummary, AccountPanelViewModel, Task>? beforeOpen = null,
        bool plus = false,
        Action? openLibrary = null)
    {
        var built = MainViewModelTests.Build();
        var now = DateTimeOffset.UtcNow;
        if (savedGames)
        {
            // Real users have games, so each row's game picker is a plain combo; an empty library makes the picker
            // wider than any real one (it grows to fit its "add games" hint).
            built.Vm.AvailableGames.Add(new FavoriteGame(8737899170, 1, "Pet Simulator 99", "", IsDefault: true, AddedAt: now));
            built.Vm.AvailableGames.Add(new FavoriteGame(920587237, 2, "Adopt Me!", "", IsDefault: false, AddedAt: now));
        }
        var names = new[] { firstName, "PetFake07", "AltFake", "BuildFake", "FarmFake" };

        for (var i = 0; i < accounts; i++)
        {
            var row = new AccountSummary(new Account(
                Id: Guid.NewGuid(), DisplayName: names[i % names.Length], AvatarUrl: "", CreatedAt: now, LastLaunchedAt: null,
                IsMain: i == 0, Facts: i == 0 ? new AccountFacts(1240, plus ? "plus" : null, 3, now.AddMinutes(-3), null) : null,
                Note: i == 0 ? note : null));
            var id = row.Id;
            var panel = new AccountPanelViewModel(
                row,
                () => Task.FromResult<IReadOnlyList<LaunchSession>>(i == 0 ? History(id, now, content) : []),
                () => Task.FromResult<IReadOnlyList<LibraryEntry>>(i == 0 ? Library(content) : []),
                _ => Task.CompletedTask,
                () => Task.CompletedTask,
                openLibrary: openLibrary ?? (() => { }));
            row.AttachPanel(panel);
            if (i == 0)
            {
                beforeOpen?.Invoke(row, panel).GetAwaiter().GetResult();
                if (expanded) panel.IsExpanded = true;
            }

            built.Vm.Accounts.Add(row);
        }

        // No SetRootDpi here, ever (2026-11-02). Every window render shares one STA host thread, and a root DPI set on
        // one window leaked into the next layout on that thread: the account row's ~91.3 DIP height then rounded to 91
        // instead of 92, so the height pins failed whenever test order put a 120 case first (every CI x64 run, and
        // locally under load). It also changed nothing this fixture's callers measured. A guard in
        // AccountRowCollapsedRenderTests fails any test source that calls it.
        return new ROROROblox.App.MainWindow(built.Vm);
    }

    private static IReadOnlyList<LaunchSession> History(Guid account, DateTimeOffset now, PanelContent content)
    {
        LaunchSession Row(string game, long place, TimeSpan ago, int minutes, Guid? server = null) =>
            new(Guid.NewGuid(), account, "Alt", null, game, place, server is not null, now - ago, now - ago + TimeSpan.FromMinutes(minutes), null, server);

        return content switch
        {
            PanelContent.Full =>
            [
                Row("Pet Simulator 99", 8737899170, TimeSpan.FromHours(2), 14 * 60),
                Row("Pet Simulator 99", 8737899170, TimeSpan.FromHours(5), 25, ClanBaseId),
                Row("Grow a Garden", 126884695634066, TimeSpan.FromDays(1), 180),
            ],
            PanelContent.Heavy =>
            [
                Row("Pet Simulator 99", 8737899170, TimeSpan.FromHours(2), 14 * 60),
                Row("Pet Simulator 99", 8737899170, TimeSpan.FromHours(5), 25, ClanBaseId),
                Row("Grow a Garden", 126884695634066, TimeSpan.FromDays(1), 180),
                Row("Blox Fruits", 2753915549, TimeSpan.FromDays(2), 40),
                Row("Adopt Me!", 920587237, TimeSpan.FromDays(3), 55),
                Row("Brookhaven", 4924922222, TimeSpan.FromDays(4), 30),
                Row("Tower of Hell", 1962086868, TimeSpan.FromDays(5), 20),
                Row("Murder Mystery 2", 142823291, TimeSpan.FromDays(6), 15),
            ],
            PanelContent.LongNames =>
            [
                Row("Pet Simulator 99 Hardcore Mega Edition (Beta Test Realm)", 8737899170, TimeSpan.FromHours(2), 14 * 60),
                Row("Grow a Garden: The Extremely Long Subtitle Nobody Asked For", 126884695634066, TimeSpan.FromDays(1), 180),
            ],
            PanelContent.UnsavedPrivate =>
            [
                Row("Pet Simulator 99", 8737899170, TimeSpan.FromHours(3), 30, Guid.NewGuid()),
            ],
            _ => [],
        };
    }

    internal static readonly Guid ClanBaseId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    private static IReadOnlyList<LibraryEntry> Library(PanelContent content)
    {
        if (content == PanelContent.LongNames)
        {
            return
            [
                new(LibraryEntryKind.Favourite, 8737899170, null, "Pet Simulator 99 Hardcore Mega Edition (Beta Test Realm)", null, null, true, null, null,
                    new LaunchTarget.Place(8737899170)),
                new(LibraryEntryKind.PrivateServer, 8737899170, ClanBaseId, "Clan base of the Northern Fox Alliance, weekly farm", null, null, false, null, null,
                    new LaunchTarget.PrivateServer(8737899170, "FAKE-CODE", PrivateServerCodeKind.AccessCode)),
            ];
        }

        if (content == PanelContent.Heavy)
        {
            var places = new (long Id, string Name)[]
            {
                (8737899170, "Pet Simulator 99"), (920587237, "Adopt Me!"), (126884695634066, "Grow a Garden"),
                (2753915549, "Blox Fruits"), (4924922222, "Brookhaven"), (1962086868, "Tower of Hell"),
            };
            var heavy = new List<LibraryEntry>();
            foreach (var (id, name) in places)
            {
                heavy.Add(new(LibraryEntryKind.Favourite, id, null, name, null, null, id == 8737899170, null, null, new LaunchTarget.Place(id)));
            }

            var servers = new (Guid Id, string Name)[]
            {
                (ClanBaseId, "Clan base"), (Guid.Parse("00000000-0000-0000-0000-0000000000c2"), "Trade room"),
                (Guid.Parse("00000000-0000-0000-0000-0000000000c3"), "Farm lobby"), (Guid.Parse("00000000-0000-0000-0000-0000000000c4"), "Test realm"),
            };
            foreach (var (sid, name) in servers)
            {
                heavy.Add(new(LibraryEntryKind.PrivateServer, 8737899170, sid, name, null, null, false, null, null,
                    new LaunchTarget.PrivateServer(8737899170, "FAKE-CODE", PrivateServerCodeKind.AccessCode)));
            }

            return heavy;
        }

        if (content != PanelContent.Full) return [];
        return
        [
            new(LibraryEntryKind.Favourite, 8737899170, null, "Pet Simulator 99", null, null, true, null, null,
                new LaunchTarget.Place(8737899170)),
            new(LibraryEntryKind.Favourite, 920587237, null, "Adopt Me!", null, null, false, null, null,
                new LaunchTarget.Place(920587237)),
            new(LibraryEntryKind.PrivateServer, 8737899170, ClanBaseId, "Clan base", null, null, false, null, null,
                new LaunchTarget.PrivateServer(8737899170, "FAKE-CODE", PrivateServerCodeKind.AccessCode)),
        ];
    }

    /// <summary>The roster's own <c>ScrollViewer</c> (the one around <c>AccountsItemsControl</c>), or throws.</summary>
    public static ScrollViewer RosterScroller(FrameworkElement content)
    {
        var items = ThemedWindowRender.Find(content, fe => fe.Name == "AccountsItemsControl", "AccountsItemsControl");
        DependencyObject? node = items;
        while (node is not null and not ScrollViewer)
        {
            node = VisualTreeHelper.GetParent(node);
        }

        return (ScrollViewer)(node ?? throw new InvalidOperationException("no ScrollViewer above AccountsItemsControl"));
    }

    /// <summary>
    /// True when the element and every ancestor are <see cref="Visibility.Visible"/>. <c>IsVisible</c> is no use
    /// here: the harness never shows the window, so nothing has a presentation source and everything reads false.
    /// </summary>
    public static bool EffectivelyVisible(FrameworkElement fe)
    {
        DependencyObject? node = fe;
        while (node is not null)
        {
            if (node is UIElement ui && ui.Visibility != Visibility.Visible) return false;
            node = VisualTreeHelper.GetParent(node);
        }

        return true;
    }

    /// <summary>
    /// A row's panel expander: the ToggleButton whose accessible name is bound to <c>PanelExpanderName</c>. Found by
    /// that binding, not by a size or a position, so it survives a resize and a move (the finder used to be
    /// <c>tb.Width == 24</c>, which broke the day the expander changed size).
    /// </summary>
    public static System.Windows.Controls.Primitives.ToggleButton Expander(FrameworkElement content) =>
        (System.Windows.Controls.Primitives.ToggleButton)ThemedWindowRender.Find(
            content,
            fe => fe is System.Windows.Controls.Primitives.ToggleButton tb && IsExpanderBinding(tb),
            "the panel expander (a ToggleButton whose accessible name binds PanelExpanderName)");

    /// <summary>Every row's panel expander, in visual-tree order (one per account row).</summary>
    public static IReadOnlyList<System.Windows.Controls.Primitives.ToggleButton> Expanders(FrameworkElement content)
    {
        var found = new List<System.Windows.Controls.Primitives.ToggleButton>();
        void Walk(DependencyObject node)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is System.Windows.Controls.Primitives.ToggleButton tb && IsExpanderBinding(tb)) found.Add(tb);
                Walk(child);
            }
        }

        Walk(content);
        return found;
    }

    /// <summary>True when <paramref name="tb"/> is the expander: its accessible name is bound to <c>PanelExpanderName</c>.</summary>
    public static bool IsExpanderBinding(System.Windows.Controls.Primitives.ToggleButton tb) =>
        tb.GetBindingExpression(System.Windows.Automation.AutomationProperties.NameProperty)?.ParentBinding.Path.Path == "PanelExpanderName";

    public static FrameworkElement ByAutomationId(FrameworkElement content, string id) =>
        ThemedWindowRender.Find(content, fe => System.Windows.Automation.AutomationProperties.GetAutomationId(fe) == id, id);
}
