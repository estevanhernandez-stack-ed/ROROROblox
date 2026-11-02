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
                var origin = actions.TransformToAncestor(scroller).Transform(new Point(0, 0));
                return new Fit(
                    scroller.ComputedVerticalScrollBarVisibility, scroller.ViewportHeight, scroller.ExtentHeight,
                    scroller.ViewportWidth, row.ActualWidth, name.ActualWidth, actions.ActualWidth,
                    origin.X + actions.ActualWidth);
            });

    /// <summary>
    /// THE LIGHT-PANEL CASE, not the cap. One account, panel open, three Recent entries, two Favourites, one
    /// private server: it fits (extent 320.8 against a 327.2 viewport at the declared 900x600). Four or more
    /// Recent entries do NOT fit: with four the extent measured 363.2, so the roster scrolls, and Recent is
    /// capped at eight, not three. Read the 3/2/1 fixture as "a light panel fits", never as the capacity. The
    /// promise spec 3.8 makes is the fresh install, below.
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
    /// The actions column at the declared 900 px, with the expander in it. Not a gate: it prints what the
    /// report needs (name and actions widths, how far the column overshoots the viewport) so the numbers are
    /// reproducible. The only assertion is the one item 7 owes: the expander adds no more than its own 32 px.
    /// </summary>
    [WindowRenderFact]
    public void TheExpanderAddsItsOwnWidthToTheActionsColumn_AndNothingElse()
    {
        foreach (var games in new[] { false, true })
        {
            var fit = Measure(96, accounts: 1, savedGames: games);
            _output.WriteLine(
                $"savedGames={games}: viewport {fit.ViewportWidth:F1}, row {fit.RowWidth:F1}, name column {fit.NameColumnWidth:F1}, "
                + $"actions {fit.ActionsWidth:F1}, actions right edge {fit.ActionsRightEdge:F1} "
                + $"(overshoot {Math.Max(0, fit.ActionsRightEdge - fit.ViewportWidth):F1})");
        }

        // The expander is 24 wide with an 8 margin; 32 is the whole delta, measured by collapsing it.
        double Actions(bool expanderShown) => ThemedWindowRender.Inspect(
            BuiltIn("brand"), "actions column with and without the expander",
            () => PanelWindowFixtures.Build(1, expanded: true, savedGames: true),
            content =>
            {
                var actions = ThemedWindowRender.Find(content,
                    fe => fe is StackPanel && Grid.GetColumn(fe) == 4 && fe.Parent is Grid g && g.RowDefinitions.Count == 2,
                    "the row's actions column");
                var expander = (FrameworkElement)ThemedWindowRender.Find(
                    content, fe => fe is System.Windows.Controls.Primitives.ToggleButton tb && tb.Width == 24, "the expander");
                expander.Visibility = expanderShown ? Visibility.Visible : Visibility.Collapsed;
                content.UpdateLayout();
                return actions.ActualWidth;
            });

        var withIt = Actions(true);
        var without = Actions(false);
        _output.WriteLine($"actions column: {without:F1} without the expander, {withIt:F1} with it, delta {withIt - without:F1}");
        Assert.Equal(32, withIt - without, precision: 0);
    }
}
