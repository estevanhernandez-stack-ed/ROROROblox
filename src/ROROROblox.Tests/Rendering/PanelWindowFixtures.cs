using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;

namespace ROROROblox.Tests.Rendering;

/// <summary>What an account's panel holds in a render fixture.</summary>
internal enum PanelContent
{
    /// <summary>Three Recent entries (a saved game, a saved private server, an unsaved game), two Favourites (one default) and one private server. Three is what fits one account at 900x600.</summary>
    Full,

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
        double layoutDpi = 96,
        Func<AccountSummary, AccountPanelViewModel, Task>? beforeOpen = null)
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
                IsMain: i == 0, Facts: i == 0 ? new AccountFacts(1240, null, 3, now.AddMinutes(-3), null) : null,
                Note: i == 0 ? note : null));
            var id = row.Id;
            var panel = new AccountPanelViewModel(
                row,
                () => Task.FromResult<IReadOnlyList<LaunchSession>>(i == 0 ? History(id, now, content) : []),
                () => Task.FromResult<IReadOnlyList<LibraryEntry>>(i == 0 ? Library(content) : []),
                _ => Task.CompletedTask,
                () => Task.CompletedTask);
            row.AttachPanel(panel);
            if (i == 0)
            {
                beforeOpen?.Invoke(row, panel).GetAwaiter().GetResult();
                if (expanded) panel.IsExpanded = true;
            }

            built.Vm.Accounts.Add(row);
        }

        var window = new ROROROblox.App.MainWindow(built.Vm);
        if (layoutDpi != 96) VisualTreeHelper.SetRootDpi(window, new DpiScale(layoutDpi / 96.0, layoutDpi / 96.0));
        return window;
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

    public static FrameworkElement ByAutomationId(FrameworkElement content, string id) =>
        ThemedWindowRender.Find(content, fe => System.Windows.Automation.AutomationProperties.GetAutomationId(fe) == id, id);
}
