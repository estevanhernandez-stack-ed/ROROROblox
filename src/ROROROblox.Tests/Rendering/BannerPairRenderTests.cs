using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ROROROblox.Core;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The two warning banners, rendered together on a real <c>MainWindow</c> under every built-in
/// theme. The last of v1.21's four eyes-on checks.
/// <para>
/// WHAT THIS ADDS OVER <c>BannerRecipeTests</c>, which already proves neither banner holds a
/// literal, both bind the same recipe, and the pair clears AA in all four themes — all from markup.
/// Two things markup cannot answer:
/// </para>
/// <list type="number">
/// <item><b>Both banners are visible AT ONCE and do not overlap.</b> They are separate Grid rows on
/// separate conditions, and "can they both be on screen together" was exactly the human check v1.21
/// wrote down. It is a layout question.</item>
/// <item><b>The <c>▲</c> actually rasterises.</b> <c>ExpiredRowRedundancyTests</c> pins the codepoint
/// IN THE MARKUP, which stays green if the font stack has no glyph for U+25B2 and WPF falls back to
/// a box or to nothing — a real risk for a symbol glyph on a machine whose fonts differ from a dev
/// box, and invisible to every markup-reading gate in the suite.</item>
/// </list>
/// <para>
/// WHAT IS STILL HUMAN, and is not faked here: whether the two banners READ as two different
/// warnings. They share one recipe now, so the difference is carried by their words and the glyph. A
/// gate asserting "the strings differ" would be green on two indistinguishable sentences and would
/// license removing the check that actually protects the reader. That stays on the capture walk.
/// </para>
/// <para>
/// THIS FILE WAS DELETED ONCE, ON A MISDIAGNOSIS, AND THE MISTAKE IS WORTH KEEPING. Its first run
/// appeared to hang and was read as a deadlock: <c>MainViewModel</c> holds eight
/// <c>Application.Current?.Dispatcher.Invoke</c> sites that no-op in the ordinary suite (where
/// <c>Application.Current</c> is null) and become real cross-thread marshals on the render host, so
/// "building more than one view model there wedges the host" was a plausible story and it was
/// written into F-100 as fact. The run had actually completed in the background: <b>both tests
/// passed, 2 of 2, in one second.</b> The elapsed time was build and restore, not rendering. The
/// underlying observation about those eight call sites is still true and still F-100; the wedging
/// was invented to explain a timeout that was never a timeout.
/// </para>
/// </summary>
public class BannerPairRenderTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public BannerPairRenderTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private const string CompatText = "Roblox 1.2.3 is newer than the last version RoRoRo was tested against.";

    /// <summary>
    /// A MainWindow with BOTH banners on. The compat banner keys off a non-empty string; the
    /// Bloxstrap banner needs a detector saying Bloxstrap IS the handler AND settings saying the
    /// warning was not dismissed, because its setter is private and the value is computed in the
    /// view model's constructor.
    /// </summary>
    private static Window BuildWindowWithBothBanners()
    {
        var built = MainViewModelTests.Build(
            settings: new MainViewModelTests.FakeAppSettings { BloxstrapWarningDismissed = false },
            bloxstrapDetector: new BloxstrapIsTheHandler());

        built.Vm.RobloxCompatBanner = CompatText;
        return new ROROROblox.App.MainWindow(built.Vm);
    }

    private static string VisibilityPath(DependencyObject o) =>
        o is Border b
            ? BindingOperations.GetBindingExpression(b, UIElement.VisibilityProperty)?.ParentBinding.Path.Path ?? ""
            : "";

    [WindowRenderFact]
    public void BothBannersRenderTogetherWithoutOverlapping()
    {
        foreach (var theme in BuiltInThemes())
        {
            var banners = ThemedWindowRender.Inspect(
                theme, $"MainWindow banners [{theme.Id}]", BuildWindowWithBothBanners, Collect);

            Assert.True(banners.Count == 2,
                $"Theme '{theme.Id}': found {banners.Count} visible banner(s), expected 2. Both are "
                + "supposed to be on screen at once — that simultaneity is the whole reason they were "
                + "given one recipe and told apart by their words.");

            var compat = banners.Single(b => b.Label == "RobloxCompatBanner");
            var bloxstrap = banners.Single(b => b.Label == "BloxstrapWarningVisible");

            _output.WriteLine($"{theme.Id,-14} compat {compat.Bounds}  fill {compat.Fill}");
            _output.WriteLine($"{theme.Id,-14} bloxs. {bloxstrap.Bounds}  fill {bloxstrap.Fill}");

            Assert.True(compat.Bounds.Bottom <= bloxstrap.Bounds.Top + 0.5,
                $"Theme '{theme.Id}': the two banners overlap. compat={compat.Bounds}, "
                + $"bloxstrap={bloxstrap.Bounds}.");

            Assert.True(compat.Fill == bloxstrap.Fill,
                $"Theme '{theme.Id}': the banners render different fills ({compat.Fill} vs "
                + $"{bloxstrap.Fill}). v1.21 item 1 ruled that they share ONE recipe and are told "
                + "apart by text and the glyph; a hue difference here re-opens that.");
        }

        static List<(string Label, Rect Bounds, string Fill)> Collect(FrameworkElement content)
        {
            var found = new List<(string, Rect, string)>();
            Walk(content);
            return found;

            void Walk(DependencyObject node)
            {
                var count = VisualTreeHelper.GetChildrenCount(node);
                for (var i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    if (child is Border b && b.ActualHeight > 0 && b.ActualWidth > 0
                        && VisibilityPath(b) is "RobloxCompatBanner" or "BloxstrapWarningVisible")
                    {
                        var o = b.TransformToAncestor(content).Transform(new Point(0, 0));
                        var fill = b.Background is SolidColorBrush s
                            ? $"#{s.Color.R:X2}{s.Color.G:X2}{s.Color.B:X2}"
                            : "(not a solid brush)";
                        found.Add((VisibilityPath(b), new Rect(o.X, o.Y, b.ActualWidth, b.ActualHeight), fill));
                    }

                    Walk(child);
                }
            }
        }
    }

    /// <summary>
    /// The glyph, in pixels. Renders the compat banner and counts accent-coloured pixels — a claim
    /// about the FONT resolving U+25B2, not about the markup carrying it.
    /// </summary>
    [WindowRenderFact]
    public void TheWarnGlyphActuallyRasterises()
    {
        foreach (var theme in BuiltInThemes())
        {
            var sample = ThemedWindowRender.MeasureRegion(
                theme,
                $"MainWindow compat banner [{theme.Id}]",
                BuildWindowWithBothBanners,
                content => ThemedWindowRender.Find(
                    content,
                    fe => VisibilityPath(fe) == "RobloxCompatBanner" && fe.ActualHeight > 0,
                    "the Border shown by RobloxCompatBanner"));

            var accent = ResolveSlot(theme, ThemeSlots.RowExpiredAccent);
            var accentPixels = sample.Histogram
                .Where(h => string.Equals(h.Colour, accent, StringComparison.OrdinalIgnoreCase))
                .Sum(h => h.Count);

            _output.WriteLine($"{theme.Id,-14} banner {sample.Width}x{sample.Height}  accent {accent} x{accentPixels}");

            Assert.True(accentPixels > 100,
                $"Theme '{theme.Id}': only {accentPixels} pixels of the accent {accent} in the "
                + "rendered compat banner. The banner's text and its ▲ are both painted in the "
                + "accent, so a count this low means they did not rasterise — a missing font for "
                + "U+25B2 looks exactly like this, and every markup-reading gate stays green through "
                + "it.\n" + sample.Describe());
        }
    }

    private static string ResolveSlot(Theme theme, string slot)
    {
        var dict = new ResourceDictionary();
        ROROROblox.App.Theming.ThemeService.ApplyTo(dict, theme, edgeAnswer: null);
        var c = ((SolidColorBrush)dict[slot]).Color;
        return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private sealed class BloxstrapIsTheHandler : IBloxstrapDetector
    {
        public bool IsBloxstrapHandler() => true;
        public bool IsStrapHandlingLaunches() => true;
    }

    private static IReadOnlyList<Theme> BuiltInThemes()
    {
        var scratch = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "rororo-banner-render-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn).ToList();

        try { if (System.IO.Directory.Exists(scratch)) System.IO.Directory.Delete(scratch, recursive: true); }
        catch (System.IO.IOException) { }

        Assert.True(themes.Count >= 4, $"Expected the four built-in themes; got {themes.Count}.");
        return themes;
    }
}
