using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ROROROblox.App.Games;
using ROROROblox.App.Localization;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// v1.36 item 9, in pixels and in the visual tree: the Library page, rendered through the real
/// <c>GamesPage</c> in every built-in theme.
/// <para>
/// WHAT THESE PROVE. That the grid virtualises (100 entries realise a screenful of containers, not 100),
/// that the Default and Private WORDS are drawn in both layouts (colour is not what carries them),
/// and that the chips' check glyph follows the view model rather than a style trigger. None of it
/// asserts a pixel height: the tile and row sizes are the page's to change, and a gate that pinned them
/// would turn the first redesign into a constant to be edited until green.
/// </para>
/// <para>
/// WHAT THEY DO NOT PROVE. That the C1 mock was matched (that is the opt-in capture and a pair of eyes at
/// C3), that the keyboard walk works on a real device, or that the dev build scrolls 100 favourites without
/// a spinner (the item's Verify line; for C3). The page is hosted in a bare window, so the tile's
/// <c>Loaded</c> never fires and no icon is ever requested here.
/// </para>
/// </summary>
public class LibraryGridRenderTests
{
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    /// <summary>
    /// Shown, by walking up and reading each <c>Visibility</c>. <c>UIElement.IsVisible</c> is false for every element of a
    /// window that was never shown (no presentation source), which is every window this harness renders, so it cannot be used.
    /// </summary>
    private static bool Shown(UIElement element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
        }

        return true;
    }

    /// <summary>The TextBlocks reading <paramref name="word"/>: realised, shown, and given room to draw.</summary>
    private static int DrawnWords(FrameworkElement root, string word)
        => Descendants<TextBlock>(root).Count(t => t.Text == word && Shown(t) && t.ActualWidth > 0 && t.ActualHeight > 0);

    [WindowRenderFact]
    public void AHundredEntries_RealiseFewerContainersThanEntries_InEveryTheme_At96Dpi()
    {
        foreach (var theme in LibraryPageFixtures.Themes())
        {
            var (entries, realised) = ThemedWindowRender.Inspect(
                theme,
                $"Library grid, 100 entries [{theme.Id}]",
                () => LibraryPageFixtures.Build(LibraryScene.Hundred).Window,
                root =>
                {
                    var grid = (ListBox)ThemedWindowRender.Find(root, fe => fe.Name == "LibraryGrid", "LibraryGrid");
                    return (grid.Items.Count, Descendants<ListBoxItem>(grid).Count());
                });

            Assert.Equal(100, entries);
            // Vacuity floor: a grid that rendered nothing also "realised fewer than 100".
            Assert.True(realised >= 4, $"[{theme.Id}] only {realised} containers were realised; the grid did not lay out.");
            Assert.True(realised < entries,
                $"[{theme.Id}] the grid realised {realised} containers for {entries} entries: virtualisation is off, and a "
                + "hundred saved games would build a hundred tiles on every visit.");
        }
    }

    [WindowRenderFact]
    public void TheGrid_DrawsTheDefaultAndPrivateWords_NotAFillAlone()
    {
        foreach (var theme in LibraryPageFixtures.Themes())
        {
            var (defaults, privates, boxes) = ThemedWindowRender.Inspect(
                theme,
                $"Library grid words [{theme.Id}]",
                () => LibraryPageFixtures.Build(LibraryScene.Showcase).Window,
                root => (
                    DrawnWords(root, Loc.Get("Shell_Panel_Tag_Default")),
                    DrawnWords(root, Loc.Get("Shell_Panel_Tag_Private")),
                    Descendants<Border>(root).Count(b => b.BorderThickness.Left > 0 && b.CornerRadius.TopLeft == 3
                        && Descendants<TextBlock>(b).Any(t => t.Text is "DEFAULT" or "PRIVATE"))));

            Assert.True(defaults == 1, $"[{theme.Id}] expected the DEFAULT word once in the grid, saw {defaults}.");
            Assert.True(privates == 1, $"[{theme.Id}] expected the PRIVATE word once in the grid, saw {privates}.");
            Assert.True(boxes >= 2, $"[{theme.Id}] the words sit in outlined tags (item 7's recipe); found {boxes}.");
        }
    }

    [WindowRenderFact]
    public void TheList_DrawsTheSameWords_AndItsColumnHeaders()
    {
        foreach (var theme in LibraryPageFixtures.Themes())
        {
            var (defaults, privates, headers, rows) = ThemedWindowRender.Inspect(
                theme,
                $"Library list words [{theme.Id}]",
                () => LibraryPageFixtures.Build(LibraryScene.Showcase, view: "list").Window,
                root =>
                {
                    var list = (ListBox)ThemedWindowRender.Find(root, fe => fe.Name == "LibraryRows", "LibraryRows");
                    var headerWords = new[] { "Library_Col_Game", "Library_Col_Kind", "Library_Col_LastPlayed", "Library_Col_Total" }
                        .Count(k => DrawnWords(root, Loc.Get(k)) == 1);
                    return (DrawnWords(root, Loc.Get("Shell_Panel_Tag_Default")),
                            DrawnWords(root, Loc.Get("Shell_Panel_Tag_Private")),
                            headerWords,
                            Descendants<ListBoxItem>(list).Count());
                });

            Assert.True(rows >= 5, $"[{theme.Id}] the list realised {rows} rows; the showcase has 7 entries.");
            Assert.True(defaults == 1, $"[{theme.Id}] expected the DEFAULT word once in the list, saw {defaults}.");
            Assert.True(privates == 1, $"[{theme.Id}] expected the PRIVATE word once in the list, saw {privates}.");
            Assert.True(headers == 4, $"[{theme.Id}] expected four column headers, saw {headers}.");
        }
    }

    [WindowRenderFact]
    public void OnlyTheChosenLayoutIsDrawn()
    {
        var brand = LibraryPageFixtures.Themes().Single(t => t.Id == "brand");
        foreach (var (view, gridShown, listShown) in new[] { ("grid", true, false), ("list", false, true) })
        {
            var (grid, list) = ThemedWindowRender.Inspect(
                brand,
                $"Library layout {view}",
                () => LibraryPageFixtures.Build(LibraryScene.Showcase, view: view).Window,
                root => (
                    Shown(ThemedWindowRender.Find(root, fe => fe.Name == "LibraryGrid", "LibraryGrid")),
                    Shown(ThemedWindowRender.Find(root, fe => fe.Name == "LibraryList", "LibraryList"))));

            Assert.Equal(gridShown, grid);
            Assert.Equal(listShown, list);
        }
    }

    [WindowRenderFact]
    public void TheCheckGlyph_FollowsTheViewModel_NotAStyleTrigger()
    {
        var flatline = LibraryPageFixtures.Themes().Single(t => t.Id == "flatline");
        var glyphs = ThemedWindowRender.Inspect(
            flatline,
            "Library chips glyph",
            () =>
            {
                var built = LibraryPageFixtures.Build(LibraryScene.Showcase, filters: "Recent,PrivateServers");
                return built.Window;
            },
            root =>
            {
                var row = (Panel)ThemedWindowRender.Find(root, fe => fe.Name == "LibraryFiltersRow", "LibraryFiltersRow");
                var chips = row.Children.OfType<ToggleButton>().ToList();
                Assert.Equal(3, chips.Count);

                bool GlyphShown(ToggleButton chip)
                    => Descendants<TextBlock>(chip).Single(t => t.Text == "✓").Visibility == Visibility.Visible;

                var before = chips.Select(c => (Checked: c.IsChecked == true, Glyph: GlyphShown(c))).ToList();

                // Flip the second chip through its own binding, the way a click does, and read the glyph again.
                chips[1].IsChecked = true;
                chips[0].IsChecked = false;
                root.UpdateLayout();
                var after = chips.Select(c => (Checked: c.IsChecked == true, Glyph: GlyphShown(c))).ToList();
                return (before, after);
            });

        Assert.Equal([(true, true), (false, false), (true, true)], glyphs.before);
        Assert.Equal([(false, false), (true, true), (true, true)], glyphs.after);
    }

    [WindowRenderFact]
    public void TheMoreButton_OpensTheFiveItemEntryMenu_InTheGridAndTheList()
    {
        var brand = LibraryPageFixtures.Themes().Single(t => t.Id == "brand");
        foreach (var view in new[] { "grid", "list" })
        {
            var menus = ThemedWindowRender.Inspect(
                brand,
                $"Library more button {view}",
                () => LibraryPageFixtures.Build(LibraryScene.Showcase, view: view).Window,
                root => Descendants<Button>(root)
                    .Where(b => b.Content as string == "⋯" && Shown(b))
                    .Select(b => b.ContextMenu?.Items.Count)
                    .ToList());

            // Four saved entries in the showcase (three favourites and a server) offer it; the two Recent places do not.
            Assert.Equal(4, menus.Count);
            Assert.All(menus, count => Assert.Equal(5, count));
        }
    }

    [WindowRenderFact]
    public void TheEmptyLibrary_ShowsTheInvitation_AndNoTiles()
    {
        var brand = LibraryPageFixtures.Themes().Single(t => t.Id == "brand");
        var (invitation, noMatch, containers) = ThemedWindowRender.Inspect(
            brand,
            "Library empty",
            () => LibraryPageFixtures.Build(LibraryScene.Empty).Window,
            root => (
                DrawnWords(root, Loc.Get("GamesPage_EmptyLibrary")),
                Shown(ThemedWindowRender.Find(root, fe => fe.Name == "NoMatchLine", "NoMatchLine")),
                Descendants<ListBoxItem>(root).Count()));

        Assert.Equal(1, invitation);
        Assert.False(noMatch);
        Assert.Equal(0, containers);
    }

    [WindowRenderFact]
    public void LongNames_TrimInsideTheTile_NeverWiderThanIt()
    {
        var brand = LibraryPageFixtures.Themes().Single(t => t.Id == "brand");
        var overflow = ThemedWindowRender.Inspect(
            brand,
            "Library long names",
            () => LibraryPageFixtures.Build(LibraryScene.LongNames).Window,
            root =>
            {
                var grid = (ListBox)ThemedWindowRender.Find(root, fe => fe.Name == "LibraryGrid", "LibraryGrid");
                var tiles = Descendants<Border>(grid).Where(b => b.Width == 128 && b.Height > 100).ToList();
                Assert.True(tiles.Count >= 2, $"expected both long-named tiles, found {tiles.Count}.");
                return tiles
                    .SelectMany(tile => Descendants<TextBlock>(tile)
                        .Where(t => Shown(t) && t.TransformToAncestor(tile).TransformBounds(new Rect(0, 0, t.ActualWidth, t.ActualHeight)).Right > tile.ActualWidth + 0.5))
                    .Select(t => t.Text)
                    .ToList();
            });

        Assert.True(overflow.Count == 0, "These texts draw past the tile's right edge: " + string.Join(" | ", overflow));
    }
}
