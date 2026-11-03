using System.Windows;
using System.Windows.Controls;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The v1.36 fit bar (spec 2026-11-01 sections 3.8, 6, 11): a one-account roster with the panel open fits the
/// declared 900x600 window with no roster scrollbar, and nothing above the fold says alts. Runs the real
/// <c>MainWindow</c> through <see cref="ThemedWindowRender"/>.
/// <para>
/// 96 AND 120 DPI. The window is laid out in device-independent units, so the same 900x600 holds at any scale;
/// what 125% changes is text metrics and layout rounding. The harness cannot reproduce a real 125% display's
/// layout (see <see cref="AccountRowCollapsedRenderTests"/>), so these gates lay out at 96 only. A 120 case that set
/// the window's root DPI was removed on 2026-11-02: it measured the same numbers as 96 and leaked its scale into the
/// shared render thread, which broke the row-height pins whenever it ran first. Layout rounding on a real 125%
/// display stays on the C2 eyes-on walk.
/// </para>
/// </summary>
public class MainWindowOneAccountFitsTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public MainWindowOneAccountFitsTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    internal static Theme BuiltIn(string id)
    {
        var scratch = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rororo-onefit-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult();
        try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, recursive: true); }
        catch (System.IO.IOException) { }

        return themes.Single(t => t.IsBuiltIn && t.Id == id);
    }

    private sealed record Fit(
        Visibility ScrollBar, double ViewportHeight, double ExtentHeight, double ViewportWidth,
        double RowWidth, double NameColumnWidth, double ActionsWidth, double ActionsRightEdge);

    private static Fit Measure(double dpi, int accounts, bool savedGames, PanelContent content = PanelContent.Full) =>
        ThemedWindowRender.Inspect(
            BuiltIn("brand"),
            $"MainWindow {accounts} account(s), panel open @{dpi}dpi",
            () => PanelWindowFixtures.Build(accounts, expanded: true, content, savedGames: savedGames),
            content =>
            {
                var scroller = PanelWindowFixtures.RosterScroller(content);
                var row = ThemedWindowRender.Find(content, fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0, "AccountRowBorder");
                var actions = ThemedWindowRender.Find(content,
                    fe => fe is StackPanel && Grid.GetColumn(fe) == 4 && fe.Parent is Grid g && g.RowDefinitions.Count == 2,
                    "the row's actions column");
                var name = ThemedWindowRender.Find(content,
                    fe => fe is StackPanel && Grid.GetColumn(fe) == 3 && fe.Parent is Grid g && g.RowDefinitions.Count == 2,
                    "the row's name column");
                // Measured from the roster's CONTENT (the ItemsControl), not the ScrollViewer: the scroller has Padding
                // "24,12,24,16", and ViewportWidth excludes it, so an edge measured from the scroller's own origin carried an
                // extra 24 DIP. Every "Remove clips by N" printed before 2026-11-02 was 24 DIP too high (C2's "14" was -10).
                var items = ThemedWindowRender.Find(content, fe => fe.Name == "AccountsItemsControl", "AccountsItemsControl");
                var origin = actions.TransformToAncestor(items).Transform(new Point(0, 0));
                return new Fit(
                    scroller.ComputedVerticalScrollBarVisibility, scroller.ViewportHeight, scroller.ExtentHeight,
                    scroller.ViewportWidth, row.ActualWidth, name.ActualWidth, actions.ActualWidth,
                    origin.X + actions.ActualWidth);
            });

    /// <summary>
    /// THE LIGHT-PANEL CASE. One account, panel open, three Recent entries, two Favourites, one private server: it fits
    /// (extent 320.8 against a 327.2 viewport at the declared 900x600). Before the C2 ruling P1 this was NOT the capacity:
    /// Recent capped at eight and four entries already scrolled (extent 363.2). Every column now caps at three, so the
    /// capacity case is <see cref="AHeavyPanel_EightRecent_SixFavourites_FourServers_ShowsThreePerColumn_AndStillFitsWithoutARosterScrollbar"/>
    /// and this one is the quiet end of the same promise. The promise spec 3.8 makes is the fresh install, below.
    /// </summary>
    [WindowRenderTheory]
    [InlineData(96)]
    public void OneAccountWithALightPanel_FitsWithoutARosterScrollbar(double dpi)
    {
        var fit = Measure(dpi, accounts: 1, savedGames: true);
        _output.WriteLine($"@{dpi}dpi: scrollbar {fit.ScrollBar}, viewport {fit.ViewportHeight:F1}, extent {fit.ExtentHeight:F1}");

        Assert.True(fit.ExtentHeight <= fit.ViewportHeight + 0.5,
            $"@{dpi}dpi: one account with its panel open needs {fit.ExtentHeight:F1} DIP of roster but the viewport is "
            + $"{fit.ViewportHeight:F1}. The one-account first screen no longer fits the declared 900x600.");
        Assert.Equal(Visibility.Collapsed, fit.ScrollBar);
    }

    [WindowRenderTheory]
    [InlineData(96)]
    public void TheFreshInstall_OnePanelOpenWithThreeEmptyStates_FitsWithoutARosterScrollbar(double dpi)
    {
        // The promise of spec 3.8: Add account on an empty profile, the new row's panel opens, no games saved, no
        // history, no note, all three groups on their one-line empty state. This is the state a first-time user sees.
        var fit = Measure(dpi, accounts: 1, savedGames: false, PanelContent.Empty);
        _output.WriteLine($"@{dpi}dpi fresh install: scrollbar {fit.ScrollBar}, viewport {fit.ViewportHeight:F1}, extent {fit.ExtentHeight:F1}");
        Assert.True(fit.ExtentHeight <= fit.ViewportHeight + 0.5,
            $"@{dpi}dpi: the fresh-install panel needs {fit.ExtentHeight:F1} DIP of roster against a {fit.ViewportHeight:F1} viewport.");
        Assert.Equal(Visibility.Collapsed, fit.ScrollBar);
    }

    [WindowRenderTheory]
    [InlineData(96)]
    public void TheBatchSurfaces_AreHiddenWithOneAccount_AndBackWithTwo(double dpi)
    {
        bool[] Visible(int accounts) => ThemedWindowRender.Inspect(
            BuiltIn("brand"),
            $"MainWindow {accounts} account(s), batch surfaces @{dpi}dpi",
            () => PanelWindowFixtures.Build(accounts, expanded: true, savedGames: true),
            content =>
            {
                bool Shown(FrameworkElement fe) => PanelWindowFixtures.EffectivelyVisible(fe) && fe.ActualWidth > 0;
                var dot = ThemedWindowRender.Find(
                    content,
                    fe => fe is System.Windows.Controls.Primitives.ToggleButton tb
                          && ReferenceEquals(tb.Style, content.TryFindResource("SelectionDotStyle")),
                    "the batches selection dot");
                return new[]
                {
                    Shown(PanelWindowFixtures.ByAutomationId(content, "DangerLaunchMultiple")),
                    Shown(PanelWindowFixtures.ByAutomationId(content, "DangerSquadLaunch")),
                    Shown(dot),
                };
            });

        Assert.All(Visible(1), shown => Assert.False(shown, "a batch surface is visible with one account"));
        Assert.All(Visible(2), shown => Assert.True(shown, "a batch surface is missing with two accounts"));
    }

    /// <summary>
    /// The C2 layout (ruling E1, 2026-11-02): the expander is the FIRST thing on the name line, inside the name column, and
    /// the actions column no longer carries it. Also prints what the report needs, reproducibly: the name and actions widths
    /// at the declared 900 px, with and without saved games, and how far the actions column's right edge overshoots the
    /// roster viewport (the old test measured the 32 px the expander used to add to that column; it moved, so the question
    /// now is whether Remove still clips). The assertions are the placement, and that the expander does not grow the name
    /// line: the collapsed row's height is pinned by <see cref="AccountRowCollapsedRenderTests"/>, and a 20 px button beside
    /// a 14 px heading would have moved it.
    /// </summary>
    [WindowRenderFact]
    public void TheExpander_IsInTheNameColumn_NotTheActionsColumn_AndDoesNotGrowTheNameLine()
    {
        foreach (var games in new[] { false, true })
        {
            var fit = Measure(96, accounts: 1, savedGames: games);
            _output.WriteLine(
                $"savedGames={games}: viewport {fit.ViewportWidth:F1}, row {fit.RowWidth:F1}, name column {fit.NameColumnWidth:F1}, "
                + $"actions {fit.ActionsWidth:F1}, actions right edge {fit.ActionsRightEdge:F1} "
                + $"(overshoot {Math.Max(0, fit.ActionsRightEdge - fit.ViewportWidth):F1})");
        }

        // TWO rows: the first account is main (its MAIN pill makes the name line 21 DIP), the second is an ordinary row (the
        // name line is the heading's own 19 DIP, which is the row AccountRowCollapsedRenderTests pins at 92). The expander has
        // to leave both lines exactly as tall as they were without it.
        var seen = ThemedWindowRender.Inspect(
            BuiltIn("brand"), "where the expander sits",
            () => PanelWindowFixtures.Build(2, expanded: true, savedGames: true),
            content =>
            {
                bool Inside(DependencyObject? node, DependencyObject ancestor)
                {
                    while (node is not null)
                    {
                        if (ReferenceEquals(node, ancestor)) return true;
                        node = System.Windows.Media.VisualTreeHelper.GetParent(node);
                    }

                    return false;
                }

                var rows = new List<(string Which, bool InName, bool InActions, bool First, bool BeforeName, double Width, double Height, double LayoutHeight,
                    double LineWith, double LineWithout, double RowWith, double RowWithout, double TextHeight, double DesiredWith, double DesiredWithout, double ColumnInner)>();
                var expanders = PanelWindowFixtures.Expanders(content);
                for (var i = 0; i < expanders.Count; i++)
                {
                    var expander = expanders[i];
                    var nameLine = (StackPanel)System.Windows.Media.VisualTreeHelper.GetParent(expander);
                    var nameText = nameLine.Children.OfType<TextBlock>().First();
                    var rowBorder = (FrameworkElement)ThemedWindowRender.Find(content,
                        fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0 && Inside(expander, fe), "the expander's AccountRowBorder");
                    // The row grid's direct child that holds the expander says which column it is in.
                    FrameworkElement? slot = null;
                    for (DependencyObject? n = expander; n is not null && slot is null; n = System.Windows.Media.VisualTreeHelper.GetParent(n))
                    {
                        if (System.Windows.Media.VisualTreeHelper.GetParent(n) is Grid g && g.RowDefinitions.Count == 2 && n is FrameworkElement fe) slot = fe;
                    }

                    var nameColumn = slot ?? throw new InvalidOperationException("the expander has no row-grid ancestor");
                    var column = Grid.GetColumn(nameColumn);
                    var (w, h) = (expander.ActualWidth, expander.ActualHeight);
                    var layoutHeight = h + expander.Margin.Top + expander.Margin.Bottom;
                    // The panel's own DesiredSize is clamped to the width it was offered (132), so it cannot show an overflow; the
                    // sum of its visible children's desired widths can.
                    double Natural() => nameLine.Children.Cast<UIElement>().Where(c => c.Visibility == Visibility.Visible).Sum(c => c.DesiredSize.Width);
                    var (lineWith, desiredWith, rowWith) = (nameLine.ActualHeight, Natural(), rowBorder.ActualHeight);
                    expander.Visibility = Visibility.Collapsed;
                    content.UpdateLayout();
                    var (lineWithout, desiredWithout, rowWithout) = (nameLine.ActualHeight, Natural(), rowBorder.ActualHeight);
                    expander.Visibility = Visibility.Visible;
                    content.UpdateLayout();
                    rows.Add((i == 0 ? "main row" : "ordinary row", column == 3, column == 4,
                        nameLine.Children.IndexOf(expander) == 0, nameLine.Children.IndexOf(expander) < nameLine.Children.IndexOf(nameText),
                        w, h, layoutHeight, lineWith, lineWithout, rowWith, rowWithout, nameText.ActualHeight, desiredWith, desiredWithout, nameColumn.ActualWidth));
                }

                return rows;
            });

        Assert.Equal(2, seen.Count);
        foreach (var r in seen)
        {
            _output.WriteLine(
                $"{r.Which}: expander {r.Width:F1}x{r.Height:F1} (layout height {r.LayoutHeight:F1}); name text {r.TextHeight:F1} tall; "
                + $"name line {r.LineWith:F2} with the expander, {r.LineWithout:F2} without; row {r.RowWith:F2} with, {r.RowWithout:F2} without");
            // What the move costs the name column, measured and printed (not asserted: the column's width is the row's, and the
            // row is wider than the declared 900 px window gives it, see the overshoot line above). The name line is a
            // StackPanel, so a name plus its MAIN pill wider than the column is clipped by the column, not trimmed.
            _output.WriteLine(
                $"{r.Which}: name line wants {r.DesiredWith:F1} DIP with the expander, {r.DesiredWithout:F1} without; the name column's inner width is {r.ColumnInner:F1} "
                + $"(over by {Math.Max(0, r.DesiredWith - r.ColumnInner):F1} with it, {Math.Max(0, r.DesiredWithout - r.ColumnInner):F1} without)");
            Assert.True(r.InName, $"{r.Which}: the expander is not inside the name column (grid column 3)");
            Assert.False(r.InActions, $"{r.Which}: the expander is still inside the actions column (grid column 4)");
            Assert.True(r.First && r.BeforeName, $"{r.Which}: the expander is not the first child of the name line, before the name");
            Assert.Equal(r.LineWithout, r.LineWith, precision: 2);
            Assert.Equal(r.RowWithout, r.RowWith, precision: 2);
        }
    }

    /// <summary>
    /// Este's C2 ruling (2026-11-02): at the declared 900 px nothing on the row clips. E1 moved the expander into the name
    /// column, which left Remove 14 DIP past the viewport and the main row's MAIN pill 10 DIP past its column; the game
    /// picker's MinWidth went from 160 to 132 to give both back. Asserted with and without saved games (the empty picker
    /// grows to fit its hint), on the main row, whose name line is the widest (expander, name, MAIN pill).
    /// </summary>
    [WindowRenderFact]
    public void AtTheDeclared900px_RemoveAndTheMainPillBothFit()
    {
        foreach (var games in new[] { false, true })
        {
            var fit = Measure(96, accounts: 1, savedGames: games);
            _output.WriteLine($"savedGames={games}: actions right edge {fit.ActionsRightEdge:F1} against a {fit.ViewportWidth:F1} viewport "
                + $"(roster scrollbar {fit.ScrollBar}, extent {fit.ExtentHeight:F1} of {fit.ViewportHeight:F1})");
            Assert.True(fit.ActionsRightEdge <= fit.ViewportWidth + 0.5,
                $"savedGames={games}: the actions column ends at {fit.ActionsRightEdge:F1} DIP, past the {fit.ViewportWidth:F1} DIP viewport, so Remove clips.");

            var (wants, has) = ThemedWindowRender.Inspect(
                BuiltIn("brand"), $"main row name line, savedGames={games}",
                () => PanelWindowFixtures.Build(1, expanded: false, savedGames: games),
                content =>
                {
                    var nameLine = (StackPanel)System.Windows.Media.VisualTreeHelper.GetParent(PanelWindowFixtures.Expanders(content)[0]);
                    var column = (FrameworkElement)System.Windows.Media.VisualTreeHelper.GetParent(nameLine);
                    var natural = nameLine.Children.Cast<UIElement>().Where(c => c.Visibility == Visibility.Visible).Sum(c => c.DesiredSize.Width);
                    return (natural, column.ActualWidth);
                });
            _output.WriteLine($"savedGames={games}: the main row's name line wants {wants:F1} DIP of a {has:F1} DIP column");
            Assert.True(wants <= has + 0.5,
                $"savedGames={games}: the main row's name line (expander, name, MAIN pill) wants {wants:F1} DIP but its column is {has:F1}; the pill clips.");
        }
    }

    /// <summary>
    /// The C2 cap (ruling P1): the HEAVIEST panel the caps allow, eight Recent sessions, six Favourites and four saved
    /// private servers, still fits the declared 900x600 with one account and no roster scrollbar, because every column
    /// shows at most three entries and the totals live in the group headers beside a See all link (no extra height). Before
    /// the cap, four Recent entries already scrolled (extent 363.2 against a 327.2 viewport). The panel's own claims are
    /// asserted too, so a green run cannot be a fixture that never filled: nine entries show (three per column), the two
    /// totals read, and both See all links are there.
    /// </summary>
    [WindowRenderTheory]
    [InlineData(96)]
    public void AHeavyPanel_EightRecent_SixFavourites_FourServers_ShowsThreePerColumn_AndStillFitsWithoutARosterScrollbar(double dpi)
    {
        var seen = ThemedWindowRender.Inspect(
            BuiltIn("brand"),
            $"MainWindow heavy panel @{dpi}dpi",
            () => PanelWindowFixtures.Build(1, expanded: true, PanelContent.Heavy, savedGames: true, plus: true),
            content =>
            {
                var scroller = PanelWindowFixtures.RosterScroller(content);
                var panel = ThemedWindowRender.Find(content, fe => fe.Name == "AccountPanelBorder", "AccountPanelBorder");

                var launches = new List<string>();
                var seeAll = new List<string>();
                var seeAllInTabOrder = new List<bool>();
                var texts = new List<string>();
                void Walk(DependencyObject node)
                {
                    for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                    {
                        var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                        if (child is Button b && PanelWindowFixtures.EffectivelyVisible(b))
                        {
                            var automationName = System.Windows.Automation.AutomationProperties.GetName(b);
                            if (System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "DangerPanelLaunch") launches.Add(automationName);
                            if (automationName.StartsWith("See all", StringComparison.Ordinal))
                            {
                                seeAll.Add(automationName);
                                seeAllInTabOrder.Add(b.Focusable && b.IsTabStop);
                            }
                        }

                        if (child is TextBlock tb && PanelWindowFixtures.EffectivelyVisible(tb) && tb.Text.Length > 0) texts.Add(tb.Text);
                        Walk(child);
                    }
                }

                Walk(panel);
                var row = ThemedWindowRender.Find(content, fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0, "AccountRowBorder");
                var heading = ThemedWindowRender.Find(content, fe => fe is TextBlock { Text: "FAVOURITES" }, "the Favourites heading");
                var recentHeading = ThemedWindowRender.Find(content, fe => fe is TextBlock { Text: "RECENT" }, "the Recent heading");
                var headerHeights = ((FrameworkElement)System.Windows.Media.VisualTreeHelper.GetParent(heading), recentHeading);
                _output.WriteLine(
                    $"row {row.ActualHeight:F1}, panel {panel.ActualHeight:F1}, Favourites header line {headerHeights.Item1.ActualHeight:F1}, Recent header {headerHeights.recentHeading.ActualHeight:F1}");
                return (scroller.ComputedVerticalScrollBarVisibility, scroller.ViewportHeight, scroller.ExtentHeight, launches, seeAll, seeAllInTabOrder, texts);
            });

        _output.WriteLine($"@{dpi}dpi heavy panel: scrollbar {seen.ComputedVerticalScrollBarVisibility}, viewport {seen.ViewportHeight:F1}, extent {seen.ExtentHeight:F1}");

        Assert.Equal(9, seen.launches.Count);                                  // three entries in each of the three columns
        Assert.DoesNotContain(seen.launches, n => n.StartsWith("Launch Blox Fruits", StringComparison.Ordinal));   // the fourth Recent entry is held back by the cap
        Assert.Contains("· 6", seen.texts);                                    // Favourites total
        Assert.Contains("· 4", seen.texts);                                    // Private servers total
        Assert.DoesNotContain(seen.texts, t => t.StartsWith("· ", StringComparison.Ordinal) && t is not ("· 6" or "· 4"));   // Recent carries no count
        Assert.Equal(new[] { "See all favourites", "See all private servers" }, seen.seeAll);
        Assert.All(seen.seeAllInTabOrder, inTabOrder => Assert.True(inTabOrder, "a See all link is not keyboard reachable"));
        Assert.True(seen.ExtentHeight <= seen.ViewportHeight + 0.5,
            $"@{dpi}dpi: a heavy panel (8 Recent, 6 Favourites, 4 private servers, capped to three per column) needs "
            + $"{seen.ExtentHeight:F1} DIP of roster but the viewport is {seen.ViewportHeight:F1}. The caps no longer hold the panel to the declared 900x600.");
        Assert.Equal(Visibility.Collapsed, seen.ComputedVerticalScrollBarVisibility);
    }
}
