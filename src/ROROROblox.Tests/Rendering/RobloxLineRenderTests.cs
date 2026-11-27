using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.KnownIssues;
using ROROROblox.App.Localization;
using ROROROblox.App.Notices;
using ROROROblox.App.ViewModels;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Feeds;
using ROROROblox.Core.KnownIssues;
using ROROROblox.Core.Theming;
using ROROROblox.Tests.KnownIssues;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The v1.41 "nothing moves" bar for the notice area (spec 2026-11-26 sections 16 and 19, item 1): with no
/// Roblox-side notice true and every panel collapsed, rows 2 to 5 of the real <c>MainWindow</c> are zero tall and
/// the roster starts where it did in v1.40.
/// <para>
/// THE CONSTANTS BELOW WERE MEASURED ON v1.40 (commit 4fae67e, no source change in the cycle yet), BEFORE the first
/// XAML change. They are never re-measured to make a failing test pass: a red here means the quiet window moved, and
/// it is only allowed to move by being fixed back. Changing a constant is a ruling, not a repair.
/// </para>
/// <para>
/// WHAT IS PINNED. Layout bounds in device-independent units, not PNG bytes, so the pin holds on both CI
/// architectures. Rows 2 to 5 are the direct children of the window's row grid in those rows; the roster's top
/// offset is the top of grid row 6 relative to the content root. Default width is the declared 900 px; 380 px is
/// compact mode (<c>MainViewModel.IsCompact</c>, <c>MainWindow.CompactWidth</c>). The harness arranges the content inside the window chrome, so the
/// content is 887 and 365 DIP wide; those are pinned too, so a green run proves the width it names.
/// </para>
/// </summary>
public class RobloxLineRenderTests
{
    /// <summary>v1.40 (4fae67e) height of each of rows 2 to 5 with nothing true, at the default width.</summary>
    internal const double V140NoticeRowHeight = 0;

    /// <summary>v1.40 (4fae67e) top of grid row 6 (the roster) from the content root, at the default 900 px width.</summary>
    internal const double V140RosterTopDefault = 161;

    /// <summary>v1.40 (4fae67e) top of grid row 6 (the roster) from the content root, at the compact 380 px width.</summary>
    internal const double V140RosterTopCompact = 48;

    /// <summary>v1.40 (4fae67e) arranged content width at the declared 900 px window: the harness arranges the content inside the window's chrome.</summary>
    internal const double V140ContentWidthDefault = 887;

    /// <summary>v1.40 (4fae67e) arranged content width in compact mode (window 380 px); proves the compact width was applied.</summary>
    internal const double V140ContentWidthCompact = 365;

    private sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_CAPTURE_ROBLOX_LINE") != "1")
            {
                Skip = "Opt-in capture: set RORORO_CAPTURE_ROBLOX_LINE=1 to write the baseline PNGs under docs/cycles/2026-11-26-roblox-feeds/mocks/baseline/.";
            }
        }
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public RobloxLineRenderTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private sealed record Bounds(double[] RowHeights, double RosterTop, double Width);

    private static Window Build(bool compact)
    {
        MainViewModel? vm = null;
        var window = PanelWindowFixtures.Build(accounts: 1, expanded: false, configureVm: v => vm = v);
        if (compact)
        {
            vm!.IsCompact = true;
        }

        return window;
    }

    private static Grid RowGrid(FrameworkElement content) =>
        content is Grid { RowDefinitions.Count: 8 } root
            ? root
            : (Grid)ThemedWindowRender.Find(
                content, fe => fe is Grid g && g.RowDefinitions.Count == 8 && g.Children.Count > 6, "the window's eight-row grid");

    private static Bounds Measure(bool compact) =>
        ThemedWindowRender.Inspect(
            MainWindowOneAccountFitsTests.BuiltIn("brand"),
            $"MainWindow notice area, quiet, compact={compact}",
            () => Build(compact),
            content =>
            {
                var grid = RowGrid(content);
                var heights = new double[4];
                for (var row = 2; row <= 5; row++)
                {
                    var children = grid.Children.OfType<FrameworkElement>().Where(c => Grid.GetRow(c) == row).ToList();
                    Assert.NotEmpty(children);
                    heights[row - 2] = children.Sum(c => c.ActualHeight);
                }

                var roster = grid.Children.OfType<FrameworkElement>().Single(c => Grid.GetRow(c) == 6);
                var top = roster.TransformToAncestor(content).Transform(new Point(0, 0)).Y;
                return new Bounds(heights, top, content.ActualWidth);
            });

    private void Pin(bool compact, double expectedRosterTop, double expectedWidth)
    {
        var b = Measure(compact);
        _output.WriteLine($"compact={compact}: width {b.Width:F2}, rows 2-5 [{string.Join(", ", b.RowHeights.Select(h => h.ToString("F2")))}], roster top {b.RosterTop:F2}");

        Assert.True(Math.Abs(b.Width - expectedWidth) < 0.01,
            $"compact={compact}: the content is {b.Width:F2} DIP wide; v1.40 (4fae67e) measured {expectedWidth}. The width under test is not the width pinned.");
        for (var i = 0; i < 4; i++)
        {
            Assert.True(b.RowHeights[i] == V140NoticeRowHeight,
                $"compact={compact}: grid row {i + 2} is {b.RowHeights[i]:F2} DIP tall with no notice true; v1.40 (4fae67e) measured {V140NoticeRowHeight}. The quiet notice area moved.");
        }

        Assert.True(Math.Abs(b.RosterTop - expectedRosterTop) < 0.01,
            $"compact={compact}: the roster starts {b.RosterTop:F2} DIP from the top; v1.40 (4fae67e) measured {expectedRosterTop}. The roster moved.");
    }

    [WindowRenderFact]
    public void TheQuietNoticeArea_IsZeroTall_AndTheRosterStartsWhereItDid_AtTheDefaultWidth() =>
        Pin(compact: false, V140RosterTopDefault, V140ContentWidthDefault);

    [WindowRenderFact]
    public void TheQuietNoticeArea_IsZeroTall_AndTheRosterStartsWhereItDid_At380px() =>
        Pin(compact: true, V140RosterTopCompact, V140ContentWidthCompact);

    [OptInFact]
    public void WriteTheBaselineCaptures_AtTheDefaultWidthAnd380px()
    {
        var dir = BaselineDir();
        foreach (var compact in new[] { false, true })
        {
            var png = ThemedWindowRender.Inspect(
                MainWindowOneAccountFitsTests.BuiltIn("brand"),
                $"MainWindow baseline capture, compact={compact}",
                () => Build(compact),
                content =>
                {
                    var w = (int)Math.Round(content.ActualWidth);
                    var h = (int)Math.Round(content.ActualHeight);
                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(
                            content.TryFindResource("BgBrush") as Brush ?? throw new InvalidOperationException("BgBrush did not resolve."),
                            null, new Rect(0, 0, w, h));
                        dc.DrawRectangle(
                            new VisualBrush(content) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                            null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
                    }

                    var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    return stream.ToArray();
                });

            var path = Path.Combine(dir, compact ? "v140-quiet-380.png" : "v140-quiet-900.png");
            File.WriteAllBytes(path, png);
            _output.WriteLine($"{path} ({png.Length} bytes)");
        }
    }

    // ---- v1.41 item 11: the line, drawn ------------------------------------------------------------------------
    //
    // Everything above this comment is item 1's quiet-state bar and is unchanged. What follows draws the five notices
    // through the real MainWindow, the way the window draws them: a scene sets the view model's inputs (or hands the
    // line a hand-built state, which is how a drift notice is drawn while the app feeds the drift banner nothing),
    // the window is arranged under a built-in theme, and the tests read the arranged tree.

    private static readonly RobloxStatusReading PartialJoin = new(RobloxStatusRow.GameJoin, RobloxStatusLevel.PartialOutage, 0);

    private static readonly RobloxUpdateState NewerUpdate = new(RobloxUpdateKind.Pending, RobloxUpdateDirection.Newer);

    private static readonly RobloxUpdateState Rollback = new(RobloxUpdateKind.Pending, RobloxUpdateDirection.Older);

    internal static readonly LineScene Contested = new() { Contested = true };

    internal static readonly LineScene Outage = new() { Status = PartialJoin };

    internal static readonly LineScene KnownIssueOnly = new() { KnownIssue = true };

    /// <summary>The drift banner feeds the line nothing in the app (ruled "leave", 2026-11-26), so this notice is handed over whole. The words are a stand-in.</summary>
    internal static readonly LineScene Drift = new()
    {
        Synthetic = new RobloxLineState(
            new RobloxNotice(RobloxNoticeKind.Drift, "Roblox changed something RoRoRo checks. Launching may not work until RoRoRo catches up.", RobloxNoticeTone.Warning, WithLink: false),
            []),
    };

    internal static readonly LineScene UpdateNewer = new() { Update = NewerUpdate };

    internal static readonly LineScene UpdateRollback = new() { Update = Rollback };

    /// <summary>An outage on top, a known issue and an update behind it: "+2 more".</summary>
    internal static readonly LineScene ThreeTrue = new() { Status = PartialJoin, KnownIssue = true, Update = NewerUpdate };

    internal sealed record LineScene
    {
        public bool Contested { get; init; }

        public RobloxStatusReading Status { get; init; } = RobloxStatusReading.None;

        public bool KnownIssue { get; init; }

        public RobloxUpdateState Update { get; init; } = RobloxUpdateState.Unknown;

        public int Accounts { get; init; } = 2;

        public bool Expanded { get; init; }

        public RobloxLineState? Synthetic { get; init; }
    }

    /// <summary>
    /// The real window with <paramref name="scene"/> applied after it exists and after compact is set, so every
    /// recompute the window itself triggers has already happened and a hand-built state is not overwritten.
    /// </summary>
    internal static Window BuildScene(LineScene scene, bool compact)
    {
        MainViewModel? vm = null;
        var window = PanelWindowFixtures.Build(
            accounts: scene.Accounts, expanded: false, configureVm: v => vm = v, uiDispatcher: new KnownIssuesNoticeModelTests.InlineDispatcher());
        if (compact)
        {
            vm!.IsCompact = true;
        }

        // The harness arranges the content at its DESIRED width, and wrapping text desires all it is offered, so a state with a
        // long sentence would be arranged up to the window's 380 or 900 and not the 365 or 887 the real window leaves it
        // (V140ContentWidth*). Pin the content to the real width so wraps are measured where they happen.
        ((FrameworkElement)window.Content).MaxWidth = compact ? V140ContentWidthCompact : V140ContentWidthDefault;

        if (scene.Contested)
        {
            vm!.SetContested(true);
        }

        if (scene.KnownIssue)
        {
            var model = new KnownIssuesNoticeModel(
                new KnownIssuesDismissals(Path.Combine(Path.GetTempPath(), "rororo-line-" + Guid.NewGuid().ToString("N") + ".json")),
                () => null,
                new KnownIssuesNoticeModelTests.InlineDispatcher());
            model.Apply(new KnownIssuesSnapshot([KnownIssueBuilder.Issue("a")], KnownIssuesSource.Release, DateTimeOffset.UnixEpoch));
            vm!.KnownIssuesNotice = model;
        }

        vm!.SetRobloxFeedReadings(scene.Update, scene.Status);
        if (scene.Synthetic is not null)
        {
            vm.RobloxLine.Apply(scene.Synthetic);
        }

        vm.RobloxLine.IsExpanded = scene.Expanded;
        return window;
    }

    /// <summary>The row 3 strip: the one Border in the window's eight-row grid that holds the status rows.</summary>
    internal static FrameworkElement Strip(FrameworkElement content) =>
        ThemedWindowRender.Find(
            content, fe => fe is Border b && Grid.GetRow(b) == 3 && b.Parent is Grid { RowDefinitions.Count: 8 }, "the row 3 strip");

    private static List<T> Visible<T>(FrameworkElement root) where T : FrameworkElement
    {
        var found = new List<T>();
        void Walk(DependencyObject node)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is T t && PanelWindowFixtures.EffectivelyVisible(t)) found.Add(t);
                Walk(child);
            }
        }

        Walk(root);
        return found;
    }

    private static double StripHeight(Theme theme, LineScene scene, bool compact) =>
        ThemedWindowRender.Inspect(theme, $"Roblox line strip height, compact={compact}", () => BuildScene(scene, compact), content => Strip(content).ActualHeight);

    private static string ButtonLabel(ButtonBase b) => b.Content as string ?? throw new InvalidOperationException($"a {b.GetType().Name} in the line has no text label");

    /// <summary>
    /// A notice's words as drawn. The text is a glyph Run and a bound Run, so <c>TextBlock.Text</c> reads empty on it
    /// (it only reflects a Text property, not Inlines).
    /// </summary>
    internal static string TextOf(TextBlock t) => string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));

    private static bool IsNoticeText(TextBlock t) => TextOf(t).StartsWith('▲');

    [WindowRenderFact]
    public void EachNotice_WhenDrawn_HasTheActionsItHasToday()
    {
        var cases = new (string Name, LineScene Scene, string[] Buttons)[]
        {
            ("contested", Contested, [Loc.Get("MainWindow_CloseRobloxForMe"), Loc.Get("MainWindow_Retry")]),
            ("outage", Outage, [Loc.Get("RobloxLine_StatusLink")]),
            ("known issue", KnownIssueOnly, [Loc.Get("MainWindow_SeeKnownIssues"), Loc.Get("MainWindow_Dismiss")]),
            ("drift", Drift, []),
            ("update", UpdateNewer, [Loc.Get("RobloxLine_WhatChanged")]),
            ("update, a rollback", UpdateRollback, []),
        };

        foreach (var (name, scene, expected) in cases)
        {
            var drawn = ThemedWindowRender.Inspect(
                MainWindowOneAccountFitsTests.BuiltIn("brand"),
                $"Roblox line, {name}",
                () => BuildScene(scene, compact: false),
                content => Visible<ButtonBase>(Strip(content))
                    .Where(b => b is not ToggleButton)
                    .OrderBy(b => b.TransformToAncestor(content).Transform(new Point(0, 0)).X)
                    .Select(ButtonLabel)
                    .ToArray());
            Assert.True(expected.SequenceEqual(drawn), $"{name}: expected buttons [{string.Join(", ", expected)}], drawn [{string.Join(", ", drawn)}]");
        }
    }

    [WindowRenderFact]
    public void EveryButtonOfTheLine_IsWiredToACommandOfTheViewModel()
    {
        var wired = ThemedWindowRender.Inspect(
            MainWindowOneAccountFitsTests.BuiltIn("brand"),
            "Roblox line, every button",
            () => BuildScene(new LineScene { Contested = true, Status = PartialJoin, KnownIssue = true, Update = NewerUpdate, Expanded = true }, compact: false),
            content => Visible<ButtonBase>(Strip(content))
                .Where(b => b is not ToggleButton)
                .Select(b => (Label: ButtonLabel(b), Wired: b.Command is not null && b.IsEnabled))
                .ToArray());

        string[] expected =
        [
            Loc.Get("MainWindow_CloseRobloxForMe"), Loc.Get("MainWindow_Retry"), Loc.Get("RobloxLine_StatusLink"),
            Loc.Get("MainWindow_SeeKnownIssues"), Loc.Get("MainWindow_Dismiss"), Loc.Get("RobloxLine_WhatChanged"),
        ];
        Assert.Equal(expected.Order(), wired.Select(w => w.Label).Order());
        Assert.All(wired, w => Assert.True(w.Wired, $"'{w.Label}' has no live command"));
    }

    [WindowRenderFact]
    public void TheNoticeArea_IsExactlyAsTallWithThreeNoticesTrueAsWithOne_AtBothWidths()
    {
        // Strings short enough not to wrap at either width, so this measures the rule (the toggle takes width, never a
        // row) and not a wrap. The real sentences are held to two lines by LabelWrapMeasurementTests.
        static LineScene Short(bool more) => new()
        {
            Synthetic = new RobloxLineState(
                new RobloxNotice(RobloxNoticeKind.Outage, "Roblox is slow", RobloxNoticeTone.Warning, WithLink: true),
                more
                    ? [new RobloxNotice(RobloxNoticeKind.KnownIssue, "A known issue", RobloxNoticeTone.Info, WithLink: false),
                       new RobloxNotice(RobloxNoticeKind.Update, "An update", RobloxNoticeTone.Info, WithLink: true)]
                    : []),
        };

        var brand = MainWindowOneAccountFitsTests.BuiltIn("brand");
        foreach (var compact in new[] { false, true })
        {
            var one = StripHeight(brand, Short(more: false), compact);
            var three = StripHeight(brand, Short(more: true), compact);
            _output.WriteLine($"compact={compact}: strip {one:F2} DIP with one notice, {three:F2} with three true");
            Assert.True(one > 0, $"compact={compact}: the strip with one notice is zero tall");
            Assert.True(Math.Abs(one - three) < 0.01, $"compact={compact}: the strip is {one:F2} DIP with one notice and {three:F2} with three true");

            var open = StripHeight(brand, Short(more: true) with { Expanded = true }, compact);
            Assert.True(open > one + 1, $"compact={compact}: opening \"+2 more\" added nothing ({one:F2} to {open:F2})");
        }
    }

    [WindowRenderFact]
    public void TheNoticeArea_WithTheRealSentences_AtTheDefaultWidth_IsAsTallWithThreeTrueAsWithOne()
    {
        var brand = MainWindowOneAccountFitsTests.BuiltIn("brand");
        var one = StripHeight(brand, Outage, compact: false);
        var three = StripHeight(brand, ThreeTrue, compact: false);
        _output.WriteLine($"default width, real sentences: strip {one:F2} DIP with the outage alone, {three:F2} with three true");
        Assert.True(Math.Abs(one - three) < 0.01, $"the strip is {one:F2} DIP with one notice and {three:F2} with three true");
    }

    [WindowRenderFact]
    public void TheToggleSpeaksItsCount_AndTabOrderIsTheTextThenItsButtonsThenTheToggle_WithNothingInATooltip()
    {
        var read = ThemedWindowRender.Inspect(
            MainWindowOneAccountFitsTests.BuiltIn("brand"),
            "Roblox line, three true, open",
            () => BuildScene(new LineScene { Status = PartialJoin, KnownIssue = true, Update = NewerUpdate, Expanded = true }, compact: false),
            content =>
            {
                var order = new List<string>();
                var tooltips = new List<string>();
                var toggleName = "";
                var toggleLabel = "";
                void Walk(DependencyObject node)
                {
                    for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    {
                        var child = VisualTreeHelper.GetChild(node, i);
                        if (child is FrameworkElement fe && PanelWindowFixtures.EffectivelyVisible(fe))
                        {
                            if (fe.ToolTip is not null) tooltips.Add(fe.GetType().Name);
                            switch (fe)
                            {
                                case ToggleButton t:
                                    toggleName = AutomationProperties.GetName(t);
                                    toggleLabel = ButtonLabel(t);
                                    order.Add("toggle");
                                    break;
                                case ButtonBase b:
                                    order.Add("button:" + ButtonLabel(b));
                                    break;
                                case TextBlock tb when IsNoticeText(tb):
                                    order.Add("text");
                                    break;
                            }
                        }

                        Walk(child);
                    }
                }

                Walk(Strip(content));
                return (order, tooltips, toggleName, toggleLabel);
            });

        // The top notice (the outage): its text, its one button, the toggle; then the two notices behind it, text then buttons.
        Assert.Equal(
            [
                "text", "button:" + Loc.Get("RobloxLine_StatusLink"), "toggle",
                "text", "button:" + Loc.Get("MainWindow_SeeKnownIssues"), "button:" + Loc.Get("MainWindow_Dismiss"),
                "text", "button:" + Loc.Get("RobloxLine_WhatChanged"),
            ],
            read.order);
        Assert.Equal("2 more Roblox notices", read.toggleName);
        Assert.Equal("+2 more", read.toggleLabel);
        Assert.Empty(read.tooltips);
    }

    [WindowRenderFact]
    public void TheCompactWrapRule_PutsTheButtonsAndTheToggleUnderTheSentence_AndOnlyAt380px()
    {
        // Este, 2026-11-26. The default width keeps the buttons to the right of the sentence (one row, tops aligned
        // to the same band); compact drops them, and the toggle with them, onto a row below it. Tab order is the
        // declaration order and is the same either way: the text, its buttons, the toggle.
        var brand = MainWindowOneAccountFitsTests.BuiltIn("brand");
        foreach (var compact in new[] { false, true })
        {
            var read = ThemedWindowRender.Inspect(
                brand,
                $"Roblox line compact wrap rule, compact={compact}",
                () => BuildScene(ThreeTrue, compact),
                content =>
                {
                    var strip = Strip(content);
                    var text = Visible<TextBlock>(strip).Single(IsNoticeText);
                    var textRect = new Rect(text.TranslatePoint(new Point(0, 0), strip), new Size(text.ActualWidth, text.ActualHeight));
                    var below = Visible<ButtonBase>(strip)
                        .Select(b => (Label: b is ToggleButton ? "toggle" : ButtonLabel(b), Top: b.TranslatePoint(new Point(0, 0), strip).Y))
                        .ToList();
                    return (textRect, below);
                });

            Assert.Equal(["button:" + Loc.Get("RobloxLine_StatusLink"), "toggle"], read.below.Select(b => b.Label is "toggle" ? "toggle" : "button:" + b.Label));
            foreach (var (label, top) in read.below)
            {
                if (compact)
                {
                    Assert.True(top >= read.textRect.Bottom - 0.5, $"compact: '{label}' starts at {top:F1}, inside the sentence (bottom {read.textRect.Bottom:F1})");
                }
                else
                {
                    Assert.True(top < read.textRect.Bottom, $"default width: '{label}' starts at {top:F1}, under the sentence (bottom {read.textRect.Bottom:F1})");
                }
            }
        }
    }

    [WindowRenderFact]
    public void UnderFlatline_EveryState_CarriesTheGlyph_AndDifferentWords()
    {
        var flatline = MainWindowOneAccountFitsTests.BuiltIn("flatline");
        var texts = new List<string>();
        foreach (var (name, scene) in new[] { ("contested", Contested), ("outage", Outage), ("known issue", KnownIssueOnly), ("drift", Drift), ("update", UpdateNewer) })
        {
            var text = ThemedWindowRender.Inspect(
                flatline,
                $"Roblox line under flatline, {name}",
                () => BuildScene(scene, compact: false),
                content => TextOf(Visible<TextBlock>(Strip(content)).Single(IsNoticeText)));
            Assert.StartsWith("▲ ", text);
            Assert.True(text.Length > 4, $"{name}: the glyph is alone");
            texts.Add(text);
        }

        Assert.Equal(5, texts.Distinct().Count());
    }

    // ---- opt-in captures for the screenshot compare against the C1 pick --------------------------------------------

    private sealed class OptInCompareFactAttribute : FactAttribute
    {
        public OptInCompareFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_CAPTURE_ROBLOX_LINE") != "1")
            {
                Skip = "Opt-in capture: set RORORO_CAPTURE_ROBLOX_LINE=1 to write line-<state>-<width>-<theme>.png under docs/cycles/2026-11-26-roblox-feeds/mocks/compare/.";
            }
        }
    }

    [OptInCompareFact]
    public void WriteTheBuiltLineCaptures_BrandAndFlatline_AtTheDefaultWidthAnd380px()
    {
        var dir = CompareDir();
        var states = new (string Name, LineScene Scene)[]
        {
            ("contested", Contested),
            ("outage", Outage),
            ("known", KnownIssueOnly),
            ("drift", Drift),
            ("update", UpdateNewer),
            ("more-collapsed", ThreeTrue),
            ("more-expanded", ThreeTrue with { Expanded = true }),
        };

        foreach (var theme in new[] { "brand", "flatline" })
        {
            foreach (var compact in new[] { false, true })
            {
                foreach (var (name, scene) in states)
                {
                    var png = ThemedWindowRender.Inspect(
                        MainWindowOneAccountFitsTests.BuiltIn(theme),
                        $"Roblox line capture {name} {theme} compact={compact}",
                        () => BuildScene(scene, compact),
                        content =>
                        {
                            var strip = Strip(content);
                            var w = (int)Math.Round(strip.ActualWidth);
                            var h = (int)Math.Round(strip.ActualHeight);
                            var visual = new DrawingVisual();
                            using (var dc = visual.RenderOpen())
                            {
                                dc.DrawRectangle(
                                    content.TryFindResource("BgBrush") as Brush ?? throw new InvalidOperationException("BgBrush did not resolve."),
                                    null, new Rect(0, 0, w, h));
                                dc.DrawRectangle(
                                    new VisualBrush(strip) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                                    null, new Rect(0, 0, strip.ActualWidth, strip.ActualHeight));
                            }

                            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(visual);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = new MemoryStream();
                            encoder.Save(stream);
                            return stream.ToArray();
                        });
                    var file = $"line-{name}-{(compact ? "380" : "900")}-{theme}.png";
                    File.WriteAllBytes(Path.Combine(dir, file), png);
                    _output.WriteLine($"{file} ({png.Length} bytes)");
                }
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
        var target = Path.Combine(dir!, "docs", "cycles", "2026-11-26-roblox-feeds", "mocks", "compare");
        Directory.CreateDirectory(target);
        return target;
    }

    private static string BaselineDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var target = Path.Combine(dir!, "docs", "cycles", "2026-11-26-roblox-feeds", "mocks", "baseline");
        Directory.CreateDirectory(target);
        return target;
    }
}
