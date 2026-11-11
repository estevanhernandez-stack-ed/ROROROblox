using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.Localization;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// v1.36 item 13: the cycle added about 104 UI strings in seven languages, and the checklist asks that the new
/// labels max at two lines at their declared widths in every language, with nothing truncated. This measures it on
/// the REAL surfaces (account row with every fact and each health state, the open panel, the Library page, the
/// tour) under en, de, es, fr, pl, pt-BR and ru, under the brand theme at 96 DPI.
/// <para>
/// The walk visits every visible, non-empty <see cref="TextBlock"/> (and any <see cref="AccessText"/> without a
/// TextBlock child). NoWrap text must not be clipped: its natural (unconstrained) width, measured on a detached
/// clone with the same font and runs, must fit the width the layout actually leaves it, which is the smaller of its
/// own slot and every clipping ancestor's slot (WPF arranges an over-wide child at its DESIRED size and clips it to
/// the slot, so <c>ActualWidth</c> alone never shows the problem). Wrapping text must need at most two lines at
/// the arranged width. Line counts come from the clone's measured height over a one-line height, not from the
/// live element, so a MaxHeight cannot hide a third line.
/// </para>
/// <para>
/// No SetRootDpi here: the DPI is 96 and stays there (a source-scan guard in AccountRowCollapsedRenderTests
/// forbids it on the shared render thread).
/// </para>
/// </summary>
[Collection("MutatesUiCulture")]
public class LabelWrapMeasurementTests
{
    private static readonly string[] Cultures = ["en", "de", "es", "fr", "pl", "pt-BR", "ru"];

    private const int MaxLines = 2;

    /// <summary>A genuine paragraph may run longer than two lines; it is still held to this sanity cap.</summary>
    private const int ParagraphCap = 6;

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public LabelWrapMeasurementTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    // ---- surfaces ---------------------------------------------------------------------------------------------

    private sealed record Surface(string Name, Func<Window> Build, Func<FrameworkElement, FrameworkElement> WalkRoot, int MinElements);

    private static FrameworkElement Roster(FrameworkElement content) =>
        ThemedWindowRender.Find(content, fe => fe.Name == "AccountsItemsControl", "AccountsItemsControl");

    /// <summary>One non-main account with every fact, saved games on the picker, and a health state shaped by <paramref name="shape"/>.</summary>
    private static Window BuildAlt(Action<AccountSummary> shape)
    {
        var built = MainViewModelTests.Build();
        var now = DateTimeOffset.UtcNow;
        built.Vm.AvailableGames.Add(new FavoriteGame(8737899170, 1, "Pet Simulator 99", "", IsDefault: true, AddedAt: now));
        built.Vm.AvailableGames.Add(new FavoriteGame(920587237, 2, "Adopt Me!", "", IsDefault: false, AddedAt: now));
        var row = new AccountSummary(new Account(
            Id: Guid.NewGuid(), DisplayName: "PetFake07", AvatarUrl: "", CreatedAt: now, LastLaunchedAt: null,
            IsMain: false, Facts: new AccountFacts(1240, "plus", 3, now.AddMinutes(-3), null),
            Note: "Farm alt, keep at 20 FPS. Clan bank on Fridays."));
        shape(row);
        var panel = new AccountPanelViewModel(
            row,
            () => Task.FromResult<IReadOnlyList<LaunchSession>>([]),
            () => Task.FromResult<IReadOnlyList<LibraryEntry>>([]),
            _ => Task.CompletedTask,
            () => Task.CompletedTask);
        row.AttachPanel(panel);
        panel.PrimeLastPlayed([new LaunchSession(Guid.NewGuid(), row.Id, "Alt", null, "Pet Simulator 99", 1L, false,
            now.AddHours(-2), now.AddHours(-1), null)]);
        built.Vm.Accounts.Add(row);
        return new ROROROblox.App.MainWindow(built.Vm);
    }

    private static readonly Surface[] Surfaces =
    [
        // One account each, with saved games, the way the declared-900 fit bar (MainWindowOneAccountFitsTests) builds
        // them. Every fact is on (Robux, Plus, friends online, last played, a note). Each health state that draws a
        // badge gets its own non-main window: the actions column is shared across rows, so an Expired row's wider
        // button stack squeezes every other row's name line (a v1.35 property), and a MAIN pill plus a badge on one
        // name line overflows the column in English too (measured, reported separately, not a translation matter).
        new("row-facts", () => PanelWindowFixtures.Build(accounts: 1, expanded: false, savedGames: true, plus: true), Roster, MinElements: 12),
        new("row-needs-verification", () => BuildAlt(r => r.SessionNeedsVerification = true), Roster, MinElements: 13),
        new("row-expired", () => BuildAlt(r => r.SessionExpired = true), Roster, MinElements: 14),
        new("row-limited-follow", () => BuildAlt(r => { r.SessionLimited = true; r.JoinViaFriend = true; }), Roster, MinElements: 15),
        new("panel-heavy", () => PanelWindowFixtures.Build(accounts: 1, content: PanelContent.Heavy, savedGames: true, plus: true), Roster, MinElements: 45),
        new("panel-empty", () => PanelWindowFixtures.Build(accounts: 1, content: PanelContent.Empty, savedGames: true), Roster, MinElements: 18),
        new("library-grid", () => LibraryPageFixtures.Build(LibraryScene.Showcase, filters: "Recent,Favourites,PrivateServers").Window, c => c, MinElements: 38),
        new("library-list", () => LibraryPageFixtures.Build(LibraryScene.Showcase, view: "list").Window, c => c, MinElements: 45),
        new("library-empty", () => LibraryPageFixtures.Build(LibraryScene.Empty).Window, c => c, MinElements: 7),
        new("library-nomatch", () => LibraryPageFixtures.Build(LibraryScene.Hundred, filters: "PrivateServers").Window, c => c, MinElements: 7),
        new("tour", () => new App.About.WelcomeWindow(), c => c, MinElements: 13),
    ];

    // ---- exemptions (named, with the reason) ------------------------------------------------------------------

    /// <summary>
    /// Genuine paragraphs: held to <see cref="ParagraphCap"/> instead of two lines. Keyed by the Text binding key
    /// where the element has one, else by surface for blocks assembled from Runs.
    /// </summary>
    private static string? ParagraphReason(string surface, string? key, string? name)
    {
        if (key is "Shell_Panel_Empty_Recent" or "Shell_Panel_Empty_Favourites" or "Shell_Panel_Empty_PrivateServers")
            return "panel empty-state sentence";
        if (key is "GamesPage_EmptyLibrary_Hint")
            return "Library empty-state invitation paragraph";
        if (name == "NoMatchLine")
            return "Library NoMatch line";
        if (surface == "tour")
            return "tour detail / tip paragraph (steps and tips are prose by design)";
        return null;
    }

    /// <summary>
    /// Labels another language clips that English does not, found by this test and NOT fixed in item 13 because the fix
    /// is a layout ruling, not a translation. Kept as a named, measured list so it cannot rot: the test fails if an
    /// entry stops reproducing (so a fix must delete it) and fails on any label not listed.
    /// </summary>
    private static readonly Dictionary<string, string> KnownOpen = new()
    {
        // Empty since 2026-11-03: Badge_Main (the MAIN pill clipped in all six translations) was fixed by making the
        // name line a Grid, so the name trims and the pill keeps its width.
    };

    /// <summary>
    /// Text that is MEANT to trim with an ellipsis when it does not fit, by binding path. Only user and game data
    /// qualifies; a localized label (a <c>[Key]</c> binding, or a view-model property that is a pure translation
    /// such as <c>KindText</c>) never does, and neither does any binding not named here.
    /// </summary>
    private static string? TrimReason(string? key) => key switch
    {
        "DisplayName" => "game / account name is user data and trims by design",
        "RenderName" => "account name is user data and trims by design",
        "MetaText" => "game name plus an age: the line's length is set by the game name, which is user data",
        "NoteFirstLine" => "the account note is user data and trims by design",
        // Item 13 ruling (2026-11-03): the status line is "In <game> · <time>" on a running row, so its length is set by
        // user data there; on a health row the facts-line badge now carries the state and its action, so a trimmed tail
        // loses nothing. The full sentence is the TextBlock's tooltip.
        "SecondaryStatusText" => "status sentence: game-name length on running rows; the badge carries the health action",
        _ => null,
    };

    /// <summary>
    /// A trimmed data name must still keep this much room (about five characters). A tag or label that squeezes the
    /// name to "Pe..." is a layout fault even though trimming is allowed.
    /// </summary>
    private const double MinTrimRoom = 48;

    // ---- the walk ---------------------------------------------------------------------------------------------

    /// <summary>A NoWrap label cut by the layout. <paramref name="Id"/> is its child-index path from the walk root, stable across cultures.</summary>
    private sealed record Clip(string Id, string? Key, string Line);

    private sealed record Result(int Checked, int Wrapping, int NoWrap, int MaxWrapLines, List<string> Offenders, List<string> Exempted, List<Clip> Clips);

    private static Result Walk(string surface, string culture, FrameworkElement content, FrameworkElement walkRoot)
    {
        var offenders = new List<string>();
        var exempted = new List<string>();
        var clips = new List<Clip>();
        int checkedCount = 0, wrapping = 0, noWrap = 0, maxWrapLines = 0;

        void Visit(DependencyObject node, string path)
        {
            var n = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is FrameworkElement fe) Inspect(fe, path + "/" + i);
                Visit(child, path + "/" + i);
            }
        }

        void Inspect(FrameworkElement fe, string path)
        {
            string text;
            TextWrapping wrap;
            TextTrimming trim;
            string? key = null;
            if (fe is TextBlock tb)
            {
                if (VisualTreeHelper.GetParent(tb) is AccessText) return; // counted via its AccessText
                text = tb.Text;
                wrap = tb.TextWrapping;
                trim = tb.TextTrimming;
                var bindPath = BindingOperations.GetBinding(tb, TextBlock.TextProperty)?.Path?.Path;
                key = bindPath;
            }
            else if (fe is AccessText at && VisualTreeHelper.GetChildrenCount(at) == 0)
            {
                text = at.Text;
                wrap = at.TextWrapping;
                trim = at.TextTrimming;
            }
            else
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text)) return;
            // An icon glyph (Segoe MDL2 / Fluent private-use code points) is not a label and has no translation.
            if (text.All(ch => char.GetUnicodeCategory(ch) == UnicodeCategory.PrivateUse)) return;
            if (!PanelWindowFixtures.EffectivelyVisible(fe)) return;
            if (fe.ActualWidth < 1 || fe.ActualHeight < 1) return;

            checkedCount++;
            var clone = Clone(fe);
            clone.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var natural = clone.DesiredSize;
            var pad = fe is TextBlock t2 ? t2.Padding : new Thickness(0);
            var oneLine = LineHeight(clone);
            var keyShort = key is not null && key.StartsWith('[') ? key.Trim('[', ']') : key;
            var id = $"{(string.IsNullOrEmpty(fe.Name) ? "-" : fe.Name)}{(keyShort is null ? "" : "/" + keyShort)}";

            if (wrap == TextWrapping.NoWrap)
            {
                noWrap++;
                var visibleRight = VisibleRight(fe, content, out var availableDesc);
                var fromLeft = RectInRoot(fe, content).Left;
                var needRight = fromLeft + natural.Width;
                if (needRight > visibleRight + 0.5)
                {
                    var detail = $"natural {natural.Width:F1} DIP, room {visibleRight - fromLeft:F1} DIP ({availableDesc})";
                    if (trim != TextTrimming.None && TrimReason(key) is { } reason)
                    {
                        exempted.Add($"[{surface}/{culture}] TRIM-OK '{text}' ({id}) {detail}: {reason}");
                        if (key == "DisplayName" && visibleRight - fromLeft < MinTrimRoom)
                        {
                            offenders.Add($"[{surface}/{culture}] SQUEEZED '{text}' ({id}) {detail}: less than {MinTrimRoom} DIP left for a name");
                        }
                    }
                    else
                    {
                        clips.Add(new Clip(path, keyShort, $"[{surface}/{culture}] CLIPPED{(trim != TextTrimming.None ? "+ELLIPSIS" : "")} '{text}' ({id}) {detail}"));
                    }
                }

                return;
            }

            wrapping++;
            var room = Math.Min(fe.ActualWidth, VisibleRight(fe, content, out _) - RectInRoot(fe, content).Left) - pad.Left - pad.Right;
            clone.Measure(new Size(Math.Max(room, 1), double.PositiveInfinity));
            var lines = (int)Math.Round((clone.DesiredSize.Height - pad.Top - pad.Bottom) / oneLine);
            maxWrapLines = Math.Max(maxWrapLines, lines);
            var paragraph = ParagraphReason(surface, keyShort, fe.Name);
            var cap = paragraph is null ? MaxLines : ParagraphCap;
            if (paragraph is not null && lines > MaxLines)
            {
                exempted.Add($"[{surface}/{culture}] PARAGRAPH {lines} lines '{Trunc(text)}' ({id}): {paragraph}");
            }

            if (lines > cap)
            {
                offenders.Add($"[{surface}/{culture}] {lines} LINES (cap {cap}) '{Trunc(text)}' ({id}) at {room:F1} DIP");
            }
        }

        Visit(walkRoot, "");
        return new Result(checkedCount, wrapping, noWrap, maxWrapLines, offenders, exempted, clips);
    }

    private static string Trunc(string s) => s.Length > 70 ? s[..70] + "..." : s;

    private static Rect RectInRoot(FrameworkElement fe, FrameworkElement root) =>
        fe.TransformToAncestor(root).TransformBounds(new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));

    /// <summary>
    /// The right edge (in <paramref name="root"/> coordinates) beyond which this element is cut: the tightest of every
    /// ancestor whose slot is narrower than what it asked for (WPF arranges it at its desired size and clips to the
    /// slot) and every ancestor that clips to its bounds. Includes the element itself.
    /// </summary>
    private static double VisibleRight(FrameworkElement fe, FrameworkElement root, out string described)
    {
        var right = double.PositiveInfinity;
        described = "unbounded";
        DependencyObject? node = fe;
        while (node is not null)
        {
            if (node is FrameworkElement a && a.ActualWidth > 0)
            {
                // The layout clip is what WPF sets when an element is arranged into less room than it asked for, so
                // it is the exact record of where the text is cut. ClipToBounds is the explicit kind.
                Rect? clip = null;
                string? why = null;
                if (LayoutInformation.GetLayoutClip(a) is { } g)
                {
                    clip = g.Bounds;
                    why = $"{a.GetType().Name}{(string.IsNullOrEmpty(a.Name) ? "" : "#" + a.Name)} layout clip";
                }
                else if (a.ClipToBounds && !ReferenceEquals(a, fe))
                {
                    clip = new Rect(0, 0, a.ActualWidth, a.ActualHeight);
                    why = $"{a.GetType().Name}{(string.IsNullOrEmpty(a.Name) ? "" : "#" + a.Name)} ClipToBounds";
                }

                if (clip is { } c)
                {
                    var r = a.TransformToAncestor(root).TransformBounds(c).Right;
                    if (r < right)
                    {
                        right = r;
                        described = why!;
                    }
                }
            }

            if (ReferenceEquals(node, root)) break;
            node = VisualTreeHelper.GetParent(node);
        }

        if (double.IsPositiveInfinity(right))
        {
            // Nothing clips it: it lives as far as it arranged.
            right = RectInRoot(fe, root).Right;
            described = "own width";
        }

        return right;
    }

    private static double LineHeight(TextBlock clone)
    {
        if (clone.LineHeight > 0 && !double.IsNaN(clone.LineHeight)) return clone.LineHeight;
        var probe = new TextBlock
        {
            Text = "Xg",
            FontFamily = clone.FontFamily, FontSize = clone.FontSize, FontWeight = clone.FontWeight,
            FontStyle = clone.FontStyle, FontStretch = clone.FontStretch,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Height;
    }

    /// <summary>A detached TextBlock with the live element's font, runs and wrapping, so measuring it changes nothing in the real tree.</summary>
    private static TextBlock Clone(FrameworkElement fe)
    {
        var c = new TextBlock
        {
            FontFamily = (FontFamily)fe.GetValue(TextBlock.FontFamilyProperty),
            FontSize = (double)fe.GetValue(TextBlock.FontSizeProperty),
            FontWeight = (FontWeight)fe.GetValue(TextBlock.FontWeightProperty),
            FontStyle = (FontStyle)fe.GetValue(TextBlock.FontStyleProperty),
            FontStretch = (FontStretch)fe.GetValue(TextBlock.FontStretchProperty),
            TextWrapping = (TextWrapping)fe.GetValue(fe is AccessText ? AccessText.TextWrappingProperty : TextBlock.TextWrappingProperty),
            LineHeight = (double)fe.GetValue(TextBlock.LineHeightProperty),
            LineStackingStrategy = (LineStackingStrategy)fe.GetValue(TextBlock.LineStackingStrategyProperty),
        };

        if (fe is TextBlock tb)
        {
            c.Padding = tb.Padding;
            if (tb.Inlines.Count > 0)
            {
                foreach (var inline in tb.Inlines) c.Inlines.Add(CloneInline(inline));
            }
            else
            {
                c.Text = tb.Text;
            }
        }
        else if (fe is AccessText at)
        {
            c.Text = at.Text;
        }

        return c;
    }

    private static Inline CloneInline(Inline source)
    {
        switch (source)
        {
            case Run r:
                return new Run(r.Text) { FontFamily = r.FontFamily, FontSize = r.FontSize, FontWeight = r.FontWeight, FontStyle = r.FontStyle, FontStretch = r.FontStretch };
            case LineBreak:
                return new LineBreak();
            case Span s:
                var span = new Span { FontFamily = s.FontFamily, FontSize = s.FontSize, FontWeight = s.FontWeight, FontStyle = s.FontStyle, FontStretch = s.FontStretch };
                foreach (var inner in s.Inlines) span.Inlines.Add(CloneInline(inner));
                return span;
            default:
                return new Run(new TextRange(source.ContentStart, source.ContentEnd).Text);
        }
    }

    // ---- the tests --------------------------------------------------------------------------------------------

    private static Theme Brand() => LibraryPageFixtures.Themes().Single(t => t.Id == "brand");

    private static Result Measure(Theme theme, Surface surface, string culture)
    {
        var previous = TranslationSource.Instance.CurrentCulture;
        TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
        try
        {
            return ThemedWindowRender.Inspect(
                theme,
                $"label wrap [{surface.Name}/{culture}]",
                surface.Build,
                content => Walk(surface.Name, culture, content, surface.WalkRoot(content)));
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }
    }

    /// <summary>
    /// The row most people see: a healthy account, no badge, at the window's DEFAULT size (900 x 600). The English
    /// version of this is <c>MainWindowOneAccountFitsTests.AtTheDeclared900px_RemoveAndTheMainPillBothFit</c>, and the
    /// badge rows are covered in every language below, but nothing measured the plain row in the six translations
    /// until captures taken on 2026-11-10 showed Remove cut off in French and German. A first-time user in those
    /// languages opens the app to a clipped button.
    /// <para>
    /// THIS IS A RATCHET, NOT A PASS. With no saved game the row's game picker stretches to fit its hint ("Add in the
    /// Library"), the translated hints are longer, and the stretch pushes Remove off the row. English fits with nothing to
    /// spare. Once a game is saved every language fits. The numbers below are the DIP of Remove that is hidden today; the
    /// test fails if any of them grows, and it also fails if one shrinks without the table being lowered, so the fix has
    /// to move it. The fix itself is a layout ruling (let the hint trim, shorten the hints, or restructure the row) and
    /// has not been made.
    /// </para>
    /// </summary>
    [WindowRenderFact]
    public void OnAHealthyRow_AtTheDefaultWidth_RemoveStaysInsideTheRow_InEverySupportedLanguage()
    {
        // Hidden DIP of Remove with no saved game, measured 2026-11-10 at 96 DPI. Lower these when the row is fixed.
        var knownOverflowWithoutSavedGames = new Dictionary<string, double>
        {
            ["en"] = 0, ["de"] = 48, ["es"] = 4, ["fr"] = 54, ["pl"] = 0, ["pt-BR"] = 19, ["ru"] = 49,
        };
        const double Slack = 2; // font rounding between machines
        var brand = Brand();
        var over = new List<string>();
        var stale = new List<string>();
        var checkedRows = 0;
        foreach (var savedGames in new[] { false, true })
        {
            foreach (var culture in Cultures)
            {
                var previous = TranslationSource.Instance.CurrentCulture;
                TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
                try
                {
                    var (right, inner) = ThemedWindowRender.Inspect(brand, $"healthy row remove edge [{culture}, savedGames={savedGames}]",
                        () => PanelWindowFixtures.Build(1, expanded: false, savedGames: savedGames), content =>
                        {
                            var row = (Border)ThemedWindowRender.Find(content, fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0, "AccountRowBorder");
                            var remove = ThemedWindowRender.Find(content,
                                fe => fe is Button && AutomationProperties.GetAutomationId(fe) == "DangerRemove" && fe.Visibility == Visibility.Visible && fe.ActualWidth > 0,
                                "the row's Remove button");
                            var edge = RectInRoot(remove, row).Right;
                            var visible = Math.Min(VisibleRight(remove, row, out _), row.ActualWidth - row.BorderThickness.Right);
                            return (edge, visible);
                        });
                    checkedRows++;
                    var hidden = Math.Max(0, right - inner);
                    var allowed = savedGames ? 0 : knownOverflowWithoutSavedGames[culture];
                    _output.WriteLine($"[{culture}, savedGames={savedGames}] Remove ends at {right:F1}, visible to {inner:F1}, hidden {hidden:F1} (known {allowed:F0})");
                    if (hidden > allowed + Slack) over.Add($"[{culture}, savedGames={savedGames}] {hidden:F1} DIP of Remove is hidden, more than the known {allowed:F0}");
                    if (hidden < allowed - Slack) stale.Add($"[{culture}, savedGames={savedGames}] only {hidden:F1} DIP is hidden now; lower the known {allowed:F0}");
                }
                finally
                {
                    TranslationSource.Instance.CurrentCulture = previous;
                }
            }
        }

        Assert.Equal(2 * Cultures.Length, checkedRows);
        Assert.True(over.Count == 0,
            "Remove is cut off further than the recorded amount on a healthy row at the default window size: " + string.Join("; ", over));
        Assert.True(stale.Count == 0,
            "The row got better and the ratchet did not move with it: " + string.Join("; ", stale));
    }

    /// <summary>
    /// Item 13 ruling (2026-11-03): on a health-badge row Re-authenticate takes Launch As's slot, and Remove stays inside
    /// the row at the declared 900 px in every language. The label walk checks text only, so a Remove whose label fits
    /// while its own border is cut off at the row edge passed it; this checks the button's box.
    /// </summary>
    [WindowRenderFact]
    public void OnEveryBadgeRow_RemoveStaysInsideTheRow_InEverySupportedLanguage()
    {
        var brand = Brand();
        var over = new List<string>();
        var checkedRows = 0;
        foreach (var surface in Surfaces.Where(s => s.Name is "row-needs-verification" or "row-expired" or "row-limited-follow"))
        {
            foreach (var culture in Cultures)
            {
                var previous = TranslationSource.Instance.CurrentCulture;
                TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
                try
                {
                    var (right, inner) = ThemedWindowRender.Inspect(brand, $"remove edge [{surface.Name}/{culture}]", surface.Build, content =>
                    {
                        var row = (Border)ThemedWindowRender.Find(content, fe => fe.Name == "AccountRowBorder" && fe.ActualHeight > 0, "AccountRowBorder");
                        var remove = ThemedWindowRender.Find(content,
                            fe => fe is Button && AutomationProperties.GetAutomationId(fe) == "DangerRemove" && fe.Visibility == Visibility.Visible && fe.ActualWidth > 0,
                            "the row's Remove button");
                        // Not the button's laid-out box: the actions Grid arranges it at its desired size and CLIPS it to the
                        // column, so the box can sit inside the row while its right side is cut (the first version of this test
                        // read the box and passed a visibly cut Remove). VisibleRight is the tightest clip on the way up.
                        var edge = RectInRoot(remove, row).Right;
                        var visible = Math.Min(VisibleRight(remove, row, out _), row.ActualWidth - row.BorderThickness.Right);
                        return (edge, visible);
                    });
                    checkedRows++;
                    _output.WriteLine($"[{surface.Name}/{culture}] Remove ends at {right:F1}, visible to {inner:F1}");
                    if (right > inner + 0.5) over.Add($"[{surface.Name}/{culture}] Remove ends at {right:F1} DIP but is cut at {inner:F1}");
                }
                finally
                {
                    TranslationSource.Instance.CurrentCulture = previous;
                }
            }
        }

        Assert.Equal(3 * Cultures.Length, checkedRows);
        Assert.True(over.Count == 0, "Remove is cut off at the row edge:\n" + string.Join("\n", over));
    }

    [WindowRenderFact]
    public void EveryLabel_MaxesAtTwoLines_AndNothingIsClipped_InEverySupportedLanguage()
    {
        var brand = Brand();
        var offenders = new List<string>();
        var exempted = new SortedSet<string>(StringComparer.Ordinal);
        var baseline = new SortedSet<string>(StringComparer.Ordinal);
        var knownOpen = new SortedSet<string>(StringComparer.Ordinal);
        var knownHit = new SortedSet<string>(StringComparer.Ordinal);
        var report = new StringBuilder();
        report.AppendLine("surface | culture | checked (wrap/nowrap) | max wrapped lines");

        foreach (var surface in Surfaces)
        {
            // English first: what English already clips at the declared 900x600 is a layout property, not a
            // translation one. A label another language clips that English does not is the offender this finds.
            var englishClipped = new HashSet<string>();
            foreach (var culture in Cultures)
            {
                var result = Measure(brand, surface, culture);
                report.AppendLine($"{surface.Name} | {culture} | {result.Checked} ({result.Wrapping}/{result.NoWrap}) | {result.MaxWrapLines}");
                offenders.AddRange(result.Offenders);
                foreach (var e in result.Exempted) exempted.Add(e);

                foreach (var clip in result.Clips)
                {
                    if (culture == "en")
                    {
                        englishClipped.Add(clip.Id);
                        baseline.Add(clip.Line);
                    }
                    else if (englishClipped.Contains(clip.Id))
                    {
                        baseline.Add(clip.Line + " (also clipped in en)");
                    }
                    else if (clip.Key is { } k && KnownOpen.TryGetValue(k, out var why))
                    {
                        knownOpen.Add(clip.Line + " :: " + why);
                        knownHit.Add(k);
                    }
                    else
                    {
                        offenders.Add(clip.Line);
                    }
                }

                // Vacuity floor: a walk that found almost nothing proves nothing.
                Assert.True(result.Checked >= surface.MinElements,
                    $"[{surface.Name}/{culture}] the walk saw only {result.Checked} text elements (floor {surface.MinElements}); it is not measuring the surface.");
            }
        }

        _output.WriteLine(report.ToString());
        _output.WriteLine("---- clipped in English too (layout at the declared 900 DIP, not translation) ----");
        foreach (var e in baseline) _output.WriteLine(e);
        _output.WriteLine("---- known open (see KnownOpen) ----");
        foreach (var e in knownOpen) _output.WriteLine(e);
        _output.WriteLine("---- exempted ----");
        foreach (var e in exempted) _output.WriteLine(e);

        Assert.True(knownHit.SetEquals(KnownOpen.Keys),
            "KnownOpen lists " + string.Join(", ", KnownOpen.Keys) + " but only " + string.Join(", ", knownHit)
            + " reproduced: a fix landed, so delete the entry.");
        Assert.True(offenders.Count == 0,
            $"{offenders.Count} label(s) clip or wrap past {MaxLines} lines:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// The walk must be able to fail. A 40-character NoWrap label in a 60 DIP slot is clipped in the shape the real
    /// surfaces would show, and a 120-character wrapping label in a narrow slot runs past two lines: both must
    /// be reported, or the passes above mean nothing.
    /// </summary>
    [WindowRenderFact]
    public void TheWalk_ReportsAClippedLabelAndAThreeLineLabel()
    {
        var result = ThemedWindowRender.Inspect(
            Brand(),
            "label wrap self-check",
            () =>
            {
                var panel = new StackPanel();
                panel.Children.Add(new Border
                {
                    Width = 60, Child = new TextBlock { Text = "A label far too long for sixty DIP of room" },
                });
                panel.Children.Add(new Border
                {
                    Width = 120,
                    Child = new TextBlock
                    {
                        Text = "This sentence is deliberately long enough to need several lines in a one hundred twenty DIP slot.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                });
                return new Window { Content = panel, Width = 300, Height = 300 };
            },
            content => Walk("self-check", "en", content, content));

        Assert.Equal(2, result.Checked);
        Assert.Single(result.Clips);
        Assert.Contains("CLIPPED", result.Clips[0].Line);
        Assert.Single(result.Offenders);
        Assert.Contains("LINES", result.Offenders[0]);
    }

    // ---- opt-in German captures -------------------------------------------------------------------------------

    private sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_CAPTURE_ROWS") != "1")
            {
                Skip = "Opt-in capture: set RORORO_CAPTURE_ROWS=1 to write the item-13 PNGs under docs/cycles/.../mocks/compare/.";
            }
        }
    }

    private static string CompareDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var target = Path.Combine(dir!, "docs", "cycles", "2026-11-01-account-library", "mocks", "compare");
        Directory.CreateDirectory(target);
        return target;
    }

    [OptInFact]
    public void WriteTheItem13Captures()
    {
        // German for the longest strings; English for the three health-badge rows, whose clipping is layout, not language.
        var brand = Brand();
        var previous = TranslationSource.Instance.CurrentCulture;
        try
        {
            foreach (var (culture, file, surface) in new[]
            {
                ("de", "row", Surfaces[0]), ("de", "panel", Surfaces[4]), ("de", "library", Surfaces[6]),
                ("en", "row-verify", Surfaces[1]), ("en", "row-expired", Surfaces[2]), ("en", "row-limited-follow", Surfaces[3]),
            })
            {
                TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
                var png = ThemedWindowRender.Inspect(brand, $"item13 capture [{file}]", surface.Build, content =>
                {
                    var target = surface.WalkRoot(content);
                    var ground = content.TryFindResource("BgBrush") as Brush
                        ?? throw new InvalidOperationException("BgBrush did not resolve; refusing to paint a white PNG.");
                    var w = (int)Math.Round(target.ActualWidth);
                    var h = (int)Math.Round(target.ActualHeight);
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(ground, null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                        dc.DrawRectangle(
                            new VisualBrush(target) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                            null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                    }

                    var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    return stream.ToArray();
                });
                var name = $"item13-{culture}-{file}-brand-96dpi.png";
                File.WriteAllBytes(Path.Combine(CompareDir(), name), png);
                _output.WriteLine($"{name} ({png.Length} bytes)");
            }
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }
    }
}
