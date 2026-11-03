using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ROROROblox.App.ViewModels;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// v1.36 item 7, the panel's placement and behaviour in the real <c>MainWindow</c> row template (spec 3.6):
/// below the header and not inside it, closed means out of layout and out of the tab order, the expander is a
/// named, focusable, styled ToggleButton that Space (the toggle pattern) drives, and every control the panel
/// adds names itself at runtime. The pixel bars live in <see cref="MainWindowOneAccountFitsTests"/> and
/// <see cref="AccountRowCollapsedRenderTests"/>. The expander is found by its accessible-name binding
/// (<see cref="PanelWindowFixtures.Expander"/>), never by its size: it moved into the name column and changed size at
/// C2 (2026-11-02).
/// </summary>
public class AccountPanelXamlTests
{
    private sealed record Snapshot(
        bool PanelIsSiblingBelowTheHeader,
        bool PanelIsInsideTheHeader,
        int PanelIndex,
        bool PanelShown,
        bool ExpanderChecked,
        bool ExpanderFocusable,
        bool ExpanderIsToggle,
        string ExpanderName,
        bool ExpanderStyledFromSecondaryToggle,
        List<(string Kind, string Name, bool Shown, bool Focusable)> PanelControls,
        bool NoteBoxShown);

    private static IEnumerable<Style> StyleChain(Style? style)
    {
        for (var st = style; st is not null; st = st.BasedOn) yield return st;
    }

    private static Snapshot Inspect(bool expanded, int accounts = 1, Func<Window>? build = null) =>
        ThemedWindowRender.Inspect(
            MainWindowOneAccountFitsTests.BuiltIn("brand"),
            $"account panel placement (expanded={expanded})",
            build ?? (() => PanelWindowFixtures.Build(accounts, expanded, savedGames: true)),
            content =>
            {
                var header = ThemedWindowRender.Find(content, fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0, "AccountRowBorder");
                var panel = ThemedWindowRender.Find(content, fe => fe.Name == "AccountPanelBorder", "AccountPanelBorder");
                var stack = (Panel)VisualTreeHelper.GetParent(header);

                bool Inside(DependencyObject? node, DependencyObject ancestor)
                {
                    while (node is not null)
                    {
                        if (ReferenceEquals(node, ancestor)) return true;
                        node = VisualTreeHelper.GetParent(node);
                    }

                    return false;
                }

                var expander = (ToggleButton)ThemedWindowRender.Find(
                    content, fe => fe is ToggleButton tb && Inside(tb, header) && PanelWindowFixtures.IsExpanderBinding(tb), "the expander");

                var controls = new List<(string, string, bool, bool)>();
                void Collect(DependencyObject node)
                {
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    {
                        var child = VisualTreeHelper.GetChild(node, i);
                        // The Button/TextBox the template names itself; skip template internals of those controls.
                        if (child is Button or ToggleButton or Wpf.Ui.Controls.TextBox)
                        {
                            var fe = (FrameworkElement)child;
                            controls.Add((child.GetType().Name, AutomationProperties.GetName(fe), PanelWindowFixtures.EffectivelyVisible(fe),
                                fe is Control c && c.Focusable && c.IsTabStop));
                            continue;
                        }

                        Collect(child);
                    }
                }

                Collect(panel);

                var noteBox = ThemedWindowRender.Find(content, fe => fe.Name == "NoteBox", "NoteBox");
                return new Snapshot(
                    PanelIsSiblingBelowTheHeader: ReferenceEquals(VisualTreeHelper.GetParent(panel), stack)
                                                  && stack.Children.IndexOf(panel) > stack.Children.IndexOf(header),
                    PanelIsInsideTheHeader: Inside(panel, header),
                    PanelIndex: stack.Children.IndexOf(panel),
                    PanelShown: PanelWindowFixtures.EffectivelyVisible(panel),
                    ExpanderChecked: expander.IsChecked == true,
                    ExpanderFocusable: expander.Focusable && expander.IsTabStop,
                    ExpanderIsToggle: true,
                    ExpanderName: AutomationProperties.GetName(expander),
                    // Styled from a ToggleButton style that carries the app's template, either directly or through BasedOn
                    // (the theme dictionary may hand back its own copy of the style, so identity with the resource is the
                    // wrong test). The expander takes PanelExpanderToggleButtonStyle directly: its glyph is bound and its open
                    // state is drawn by that template, so it needs no derived style, and a derived button style may carry
                    // Visibility/IsEnabled setters only. The style must be
                    // set EXPLICITLY (by the row template or locally): with none, WPF falls back to an implicit ToggleButton
                    // style that also carries a template, and a chain-only check passed with the Style attribute deleted (found
                    // by breaking it, 2026-11-02). ReadLocalValue is the wrong probe inside a DataTemplate: a template-set
                    // Style is BaseValueSource.ParentTemplate, not Local.
                    ExpanderStyledFromSecondaryToggle: DependencyPropertyHelper.GetValueSource(expander, FrameworkElement.StyleProperty).BaseValueSource
                                                           is BaseValueSource.ParentTemplate or BaseValueSource.Local
                                                       && StyleChain(expander.Style).Any(st =>
                                                           st.TargetType == typeof(ToggleButton)
                                                           && st.Setters.OfType<Setter>().Any(x => x.Property == Control.TemplateProperty)),
                    controls,
                    NoteBoxShown: PanelWindowFixtures.EffectivelyVisible(noteBox));
            });

    [WindowRenderFact]
    public void ThePanel_IsTheThirdChildOfTheRowStackPanel_BelowTheHeaderNotInsideIt()
    {
        var s = Inspect(expanded: true);
        Assert.False(s.PanelIsInsideTheHeader, "the panel is inside AccountRowBorder: the drag ghost would capture it");
        Assert.True(s.PanelIsSiblingBelowTheHeader);
        Assert.Equal(2, s.PanelIndex); // drop line, header, panel
    }

    [WindowRenderFact]
    public void AClosedPanel_IsOutOfLayout_AndItsControlsAreOutOfTheTabOrder()
    {
        var closed = Inspect(expanded: false);
        Assert.False(closed.PanelShown);
        Assert.NotEmpty(closed.PanelControls);                              // the scan found them, so "none shown" is not vacuous
        Assert.All(closed.PanelControls, c => Assert.False(c.Shown, $"{c.Kind} '{c.Name}' is reachable while the panel is closed"));

        var open = Inspect(expanded: true);
        Assert.True(open.PanelShown);
        // Save shows only on an unsaved Recent entry (one in the fixture); every other control is in the tab order. The
        // two See all links are the exception that proves the rule: they show only when a column hides entries (C2, P1),
        // and this light panel hides none, so both are out of layout and out of the tab order even while it is open (the
        // heavy-panel fit test, where they show, asserts they are focusable).
        Assert.All(open.PanelControls.Where(c => c.Kind != "TextBox" && !c.Name.StartsWith("Save ", StringComparison.Ordinal) && !c.Name.StartsWith("See all", StringComparison.Ordinal)),
            c => Assert.True(c.Shown && c.Focusable, $"{c.Kind} '{c.Name}' is not in the tab order while the panel is open"));
        Assert.Equal(2, open.PanelControls.Count(c => c.Name.StartsWith("See all", StringComparison.Ordinal)));   // built, so the closed-panel scan above is not vacuous for them
        Assert.All(open.PanelControls.Where(c => c.Name.StartsWith("See all", StringComparison.Ordinal)),
            c => Assert.False(c.Shown, $"'{c.Name}' shows although no column hides an entry"));
        Assert.Single(open.PanelControls, c => c.Name.StartsWith("Save ", StringComparison.Ordinal) && c.Shown);
    }

    [WindowRenderFact]
    public void TheExpander_IsAStyledNamedFocusableToggleButton_ThatTracksThePanel()
    {
        var open = Inspect(expanded: true);
        Assert.True(open.ExpanderChecked);
        Assert.True(open.ExpanderFocusable);
        Assert.True(open.ExpanderStyledFromSecondaryToggle, "the expander must take an app toggle rank explicitly (PanelExpanderToggleButtonStyle, directly or BasedOn)");
        Assert.Equal("Show recent games for NovaFake", open.ExpanderName);

        Assert.False(Inspect(expanded: false).ExpanderChecked);
    }

    /// <summary>
    /// The C2 open state (Este's call, the mock's cyan outline): an open expander draws a Cyan outline and a Cyan
    /// glyph and NO fill, a closed one draws the ordinary derived edge and a white glyph, and the press sheen is not
    /// the open state (the shared toggle template's checked state is a grey press sheen, which is exactly what this
    /// rank replaces). Read from the applied template of the real row, resolved against the theme.
    /// </summary>
    [WindowRenderFact]
    public void TheOpenExpander_IsACyanOutlineAndGlyph_NotAFill_AndAClosedOneIsTheOrdinaryEdge()
    {
        string Hex(Brush? b) => b is SolidColorBrush s ? $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : "<not a solid brush>";

        var seen = ThemedWindowRender.Inspect(
            MainWindowOneAccountFitsTests.BuiltIn("brand"), "expander open state",
            () => PanelWindowFixtures.Build(1, expanded: true, savedGames: true),
            content =>
            {
                var expander = PanelWindowFixtures.Expander(content);
                string Slot(string key) => Hex(content.TryFindResource(key) as Brush);

                (string Border, string Glyph, string Fill, Visibility PressSheen) Read()
                {
                    var chrome = (Border)expander.Template.FindName("Chrome", expander);
                    var press = (Border)expander.Template.FindName("PressSheen", expander);
                    return (Hex(chrome.BorderBrush), Hex(expander.Foreground), Hex(chrome.Background), press.Visibility);
                }

                var open = Read();
                expander.IsChecked = false;
                content.UpdateLayout();
                var closed = Read();
                return (open, closed, cyan: Slot("CyanBrush"), edge: Slot("InteractiveEdgeBrush"), white: Slot("WhiteBrush"), navy: Slot("NavyBrush"));
            });

        Assert.Equal(seen.cyan, seen.open.Border);
        Assert.Equal(seen.cyan, seen.open.Glyph);
        Assert.Equal(seen.navy, seen.open.Fill);                       // the fill is the rank's own, never repainted
        Assert.Equal(Visibility.Collapsed, seen.open.PressSheen);      // open is NOT the grey press sheen
        Assert.Equal(seen.edge, seen.closed.Border);
        Assert.Equal(seen.white, seen.closed.Glyph);
        Assert.NotEqual(seen.cyan, seen.edge);                         // vacuity: the two states are different colours
    }

    /// <summary>
    /// The C2 facts colour (F2) and its one exception. On an ordinary row Robux reads Cyan, Plus reads the lightened
    /// magenta in bold inside a Magenta outline, and friends online reads white beside a Cyan dot. On an EXPIRED row the
    /// two coloured texts fall back to white: measured against RowExpiredBg they failed AA (see
    /// <c>ContrastPairGateTests.NamedPairs</c>), and the styles carry a trigger on SessionExpired for exactly that. The outlines
    /// and the bold stay in both states. Read from the real row's text blocks, resolved against the theme.
    /// </summary>
    [WindowRenderFact]
    public void OnAnExpiredRow_TheColouredFactsFallBackToWhiteText_AndAnOrdinaryRowIsColoured()
    {
        string Hex(Brush? b) => b is SolidColorBrush s ? $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : "<not a solid brush>";

        var seen = new Dictionary<bool, (string Robux, string Plus, string Friends, string PlusOutline, string RobuxOutline, string FriendsOutline, bool PlusBold, bool DotShown)>();
        string cyan = "", limited = "", white = "", magenta = "";
        foreach (var expired in new[] { false, true })
        {
            seen[expired] = ThemedWindowRender.Inspect(
                MainWindowOneAccountFitsTests.BuiltIn("brand"), $"facts colour, expired={expired}",
                () => PanelWindowFixtures.Build(1, expanded: false, savedGames: true, plus: true,
                    beforeOpen: (row, _) => { row.SessionExpired = expired; return Task.CompletedTask; }),
                content =>
                {
                    cyan = Hex(content.TryFindResource("CyanBrush") as Brush);
                    limited = Hex(content.TryFindResource("HealthLimitedBrush") as Brush);
                    white = Hex(content.TryFindResource("WhiteBrush") as Brush);
                    magenta = Hex(content.TryFindResource("MagentaBrush") as Brush);

                    var robux = (TextBlock)ThemedWindowRender.Find(content, fe => fe is TextBlock { Text: "R$ 1,240" }, "the Robux text");
                    var plus = (TextBlock)ThemedWindowRender.Find(content, fe => fe is TextBlock { Text: "Plus" }, "the Plus text");
                    var friends = (TextBlock)ThemedWindowRender.Find(content, fe => fe is TextBlock { Text: "3 friends online" }, "the friends-online text");
                    Border Badge(DependencyObject node)
                    {
                        while (node is not null and not Border) node = VisualTreeHelper.GetParent(node);
                        return (Border)node!;
                    }

                    var dot = ThemedWindowRender.Find(content, fe => fe is System.Windows.Shapes.Ellipse e && e.Width == 7, "the friends-online dot");
                    return (Hex(robux.Foreground), Hex(plus.Foreground), Hex(friends.Foreground),
                        Hex(Badge(plus).BorderBrush), Hex(Badge(robux).BorderBrush), Hex(Badge(friends).BorderBrush),
                        plus.FontWeight == FontWeights.Bold, PanelWindowFixtures.EffectivelyVisible(dot));
                });
        }

        var ordinary = seen[false];
        Assert.Equal(cyan, ordinary.Robux);
        Assert.Equal(limited, ordinary.Plus);
        Assert.Equal(white, ordinary.Friends);                  // friends online keeps white text; its colour is the outline and the dot
        Assert.Equal(magenta, ordinary.PlusOutline);
        Assert.Equal(cyan, ordinary.RobuxOutline);
        Assert.Equal(cyan, ordinary.FriendsOutline);
        Assert.True(ordinary.PlusBold && ordinary.DotShown);

        var expiredRow = seen[true];
        Assert.Equal(white, expiredRow.Robux);                  // the two coloured texts stand down on RowExpiredBg
        Assert.Equal(white, expiredRow.Plus);
        Assert.Equal(white, expiredRow.Friends);
        Assert.Equal(magenta, expiredRow.PlusOutline);          // the outlines and the bold stay
        Assert.Equal(cyan, expiredRow.RobuxOutline);
        Assert.True(expiredRow.PlusBold);
        Assert.NotEqual(cyan, white);                           // vacuity: the two states are different colours
    }

    /// <summary>
    /// Este's call after C3 (2026-11-03, option a): the Plus badge leads with a plus glyph of our own drawing, not Roblox's
    /// hexagon mark (a Roblox trademark; the listing claims no affiliation). The glyph is decoration on a badge that already
    /// names itself, it takes the word's colour in both states (so the expired-row white fallback reaches it too), and it
    /// costs the facts line no height: the Plus badge stays exactly as tall as the Robux badge beside it.
    /// </summary>
    [WindowRenderFact]
    public void ThePlusBadge_LeadsWithOurOwnPlusGlyph_InTheWordsColour_AndAddsNoHeight()
    {
        string Hex(Brush? b) => b is SolidColorBrush s ? $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}" : "<not a solid brush>";

        foreach (var expired in new[] { false, true })
        {
            var m = ThemedWindowRender.Inspect(
                MainWindowOneAccountFitsTests.BuiltIn("brand"), $"plus glyph, expired={expired}",
                () => PanelWindowFixtures.Build(1, expanded: false, savedGames: true, plus: true,
                    beforeOpen: (row, _) => { row.SessionExpired = expired; return Task.CompletedTask; }),
                content =>
                {
                    var word = (TextBlock)ThemedWindowRender.Find(content, fe => fe is TextBlock { Text: "Plus" }, "the Plus text");
                    var line = (Panel)VisualTreeHelper.GetParent(word);
                    var glyph = (System.Windows.Shapes.Path)ThemedWindowRender.Find(content, fe => fe.Name == "PlusGlyph", "the plus glyph");
                    Border Badge(DependencyObject node)
                    {
                        while (node is not null and not Border) node = VisualTreeHelper.GetParent(node);
                        return (Border)node!;
                    }

                    var robux = (TextBlock)ThemedWindowRender.Find(content, fe => fe is TextBlock { Text: "R$ 1,240" }, "the Robux text");
                    return (First: ReferenceEquals(line.Children[0], glyph) && line.Children.IndexOf(word) == 1,
                        SameBadge: ReferenceEquals(Badge(glyph), Badge(word)),
                        Stroke: Hex(glyph.Stroke), Word: Hex(word.Foreground),
                        Unnamed: string.IsNullOrEmpty(AutomationProperties.GetName(glyph)),
                        PlusHeight: Badge(word).ActualHeight, RobuxHeight: Badge(robux).ActualHeight);
                });

            Assert.True(m.First && m.SameBadge, $"expired={expired}: the glyph is not the first thing in the Plus badge, before the word");
            Assert.Equal(m.Word, m.Stroke);
            Assert.True(m.Unnamed, $"expired={expired}: the glyph carries its own accessible name; it is decoration (a Path has no peer) and the badge already names itself");
            Assert.Equal(m.RobuxHeight, m.PlusHeight, precision: 2);
        }
    }

    [WindowRenderFact]
    public void SpaceOnTheExpander_TogglesThePanel()
    {
        // Space on a focused ToggleButton calls OnClick, which is what the UIA toggle pattern calls too
        // (TogglePatternReachesTheHandlerTests pins that equivalence for this app's toggles).
        var flipped = ThemedWindowRender.Inspect(
            MainWindowOneAccountFitsTests.BuiltIn("brand"), "expander toggle",
            () => PanelWindowFixtures.Build(1, expanded: false, savedGames: true),
            content =>
            {
                var header = ThemedWindowRender.Find(content, fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0, "AccountRowBorder");
                var row = (AccountSummary)header.DataContext;
                var expander = PanelWindowFixtures.Expander(content);
                var peer = UIElementAutomationPeer.CreatePeerForElement(expander);
                var toggle = (IToggleProvider)peer.GetPattern(PatternInterface.Toggle);

                var before = row.Panel!.IsExpanded;
                toggle.Toggle();
                var afterOne = row.Panel.IsExpanded;
                toggle.Toggle();
                return (before, afterOne, afterTwo: row.Panel.IsExpanded);
            });

        Assert.False(flipped.before);
        Assert.True(flipped.afterOne);
        Assert.False(flipped.afterTwo);
    }

    [WindowRenderFact]
    public void CompactMode_HidesTheExpanderAndThePanel_AndBringsThemBackOnExit()
    {
        // The full-mode roster collapses wholesale in compact mode (CompactAccountTemplate has no panel), so the
        // expander and panel are out of layout with it; the panel's remembered state is untouched (the store-level
        // half is MainViewModelTests.CompactMode_OnAndOff_NeverWritesPanelExpanded_AndTheStateComesBack).
        var seen = ThemedWindowRender.Inspect(
            MainWindowOneAccountFitsTests.BuiltIn("brand"), "compact mode and the panel",
            () => PanelWindowFixtures.Build(1, expanded: true, savedGames: true),
            content =>
            {
                var vm = (MainViewModel)content.DataContext;
                var panel = ThemedWindowRender.Find(content, fe => fe.Name == "AccountPanelBorder", "AccountPanelBorder");
                var expander = PanelWindowFixtures.Expander(content);
                var row = (AccountSummary)panel.DataContext;

                var full = (PanelWindowFixtures.EffectivelyVisible(panel), PanelWindowFixtures.EffectivelyVisible(expander));
                vm.IsCompact = true;
                content.UpdateLayout();
                var compact = (PanelWindowFixtures.EffectivelyVisible(panel), PanelWindowFixtures.EffectivelyVisible(expander));
                vm.IsCompact = false;
                content.UpdateLayout();
                var back = (PanelWindowFixtures.EffectivelyVisible(panel), PanelWindowFixtures.EffectivelyVisible(expander), row.Panel!.IsExpanded);
                return (full, compact, back);
            });

        Assert.Equal((true, true), seen.full);
        Assert.Equal((false, false), seen.compact);
        Assert.Equal((true, true, true), seen.back);
    }

    [WindowRenderFact]
    public void TheNoteBox_CapsAt200_AndItsBindingCommitsThroughTheRowsNormalizer()
    {
        // Enter and LostFocus both end in BindingExpression.UpdateSource (OnNoteBoxKeyDown, and the LostFocus
        // trigger on the binding), so this is the commit both paths share. The key and focus events themselves
        // need a real input device and are on C2's eyes-on walk.
        var seen = ThemedWindowRender.Inspect(
            MainWindowOneAccountFitsTests.BuiltIn("brand"), "note box commit",
            () => PanelWindowFixtures.Build(1, expanded: true, note: null, savedGames: true),
            content =>
            {
                var box = (TextBox)ThemedWindowRender.Find(content, fe => fe.Name == "NoteBox", "NoteBox");
                var row = (AccountSummary)box.DataContext;
                var trigger = BindingOperations.GetBinding(box, TextBox.TextProperty)!.UpdateSourceTrigger;

                box.Text = "   Clan bank on Fridays   ";
                BindingOperations.GetBindingExpression(box, TextBox.TextProperty)!.UpdateSource();
                return (box.MaxLength, trigger, row.Note, row.NoteText);
            });

        Assert.Equal(Core.Account.MaxNoteLength, seen.MaxLength);
        Assert.Equal(UpdateSourceTrigger.LostFocus, seen.trigger);
        Assert.Equal("Clan bank on Fridays", seen.Note);
        Assert.Equal("Clan bank on Fridays", seen.NoteText);
    }

    [WindowRenderFact]
    public void EveryControlThePanelAdds_NamesItself_AtRuntime()
    {
        var s = Inspect(expanded: true);
        Assert.True(s.PanelControls.Count >= 6, $"only {s.PanelControls.Count} panel controls found; the scan is broken, not clean");
        Assert.All(s.PanelControls, c => Assert.False(string.IsNullOrWhiteSpace(c.Name), $"{c.Kind} in the panel has no accessible name"));
        Assert.Contains(s.PanelControls, c => c.Kind == "TextBox" && c.Name == "Private note for NovaFake");
        Assert.Contains(s.PanelControls, c => c.Name == "Open Roblox home as NovaFake");
        Assert.Contains(s.PanelControls, c => c.Name.StartsWith("Launch Pet Simulator 99, ", StringComparison.Ordinal));
        Assert.Contains(s.PanelControls, c => c.Name == "Save Grow a Garden to favourites");
    }

    [WindowRenderFact]
    public void TheNoteBox_IsThereToEdit_AndAnEmptyPanelStillReadsThreeEmptyStates()
    {
        Assert.True(Inspect(expanded: true).NoteBoxShown);

        var empties = ThemedWindowRender.Inspect(
            MainWindowOneAccountFitsTests.BuiltIn("brand"), "empty states",
            () => PanelWindowFixtures.Build(1, expanded: true, PanelContent.Empty, note: null),
            content =>
            {
                var panel = ThemedWindowRender.Find(content, fe => fe.Name == "AccountPanelBorder", "AccountPanelBorder");
                var texts = new List<string>();
                void Walk(DependencyObject node)
                {
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    {
                        var child = VisualTreeHelper.GetChild(node, i);
                        if (child is TextBlock tb && PanelWindowFixtures.EffectivelyVisible(tb) && tb.Text.Length > 0) texts.Add(tb.Text);
                        Walk(child);
                    }
                }

                Walk(panel);
                return texts;
            });

        Assert.Contains("Nothing played yet. Launch a game and it shows up here.", empties);
        Assert.Contains("No favourites yet. Save one from Recent, or add games in the Library.", empties);
        Assert.Contains("No private servers saved. Paste a server link in the Library to add one.", empties);
    }
}
