using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.ViewModels;
using ROROROblox.Core.Theming;
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
