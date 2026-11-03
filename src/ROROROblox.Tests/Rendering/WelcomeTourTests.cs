using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.Localization;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// v1.36 item 10 (spec 5): the tour speaks to one account first. The reading order is the contract, so it is
/// pinned against the XAML source: the launcher hero, then step 1 (add an account), step 2 (play from the panel),
/// step 3 (alts, last), and the tips that only matter with several accounts sit under their own heading while the
/// swatch, Compact, tray and ^ tips stay above it. Source order is reading order here because every element is a
/// plain StackPanel child.
/// <para>
/// Also holds the opt-in capture of the window (env <c>RORORO_CAPTURE_ROWS=1</c>, off by default) that writes
/// <c>item10-*.png</c> under <c>docs/cycles/2026-11-01-account-library/mocks/compare/</c>.
/// </para>
/// </summary>
public class WelcomeTourTests
{
    private static string XamlPath()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!, "src", "ROROROblox.App", "About", "WelcomeWindow.xaml");
    }

    private static int At(string xaml, string key)
    {
        var m = Regex.Match(xaml, @"\{loc:Loc " + Regex.Escape(key) + @"\}");
        Assert.True(m.Success, $"WelcomeWindow.xaml no longer references {key}.");
        Assert.Single(Regex.Matches(xaml, @"\{loc:Loc " + Regex.Escape(key) + @"\}"));
        return m.Index;
    }

    [Fact]
    public void TheTourReadsLauncherFirstThenStepsThenTheMultiAccountTips()
    {
        var xaml = File.ReadAllText(XamlPath());

        var hero = At(xaml, "WelcomeWindow_OnePlaceToLaunchRoblox");
        var step1 = At(xaml, "WelcomeWindow_ClickAddAccount");
        var step2 = At(xaml, "WelcomeWindow_PickOrPlayFromThePanel");
        var step3 = At(xaml, "WelcomeWindow_WhenYouWantAlts");
        Assert.True(hero < step1, "The hero subtitle (launcher first) must come before step 1.");
        Assert.True(step1 < step2 && step2 < step3,
            "Steps must read: add an account, play from the panel, then alts last.");

        var heading = At(xaml, "WelcomeWindow_WithMoreThanOneAccount");
        Assert.True(step3 < heading, "The multi-account heading belongs after the steps.");

        foreach (var above in new[]
        {
            "WelcomeWindow_TheColoredSwatchNextTo", "WelcomeWindow_Compact",
            "WelcomeWindow_ClosingXMinimizesToTray", "WelcomeWindow_WindowsHidesNewTrayIcons",
        })
        {
            Assert.True(At(xaml, above) < heading, $"{above} is a one-account tip and must stay above the heading.");
        }

        foreach (var below in new[]
        {
            "WelcomeWindow_OnTheLeftOfA", "WelcomeWindow_StarNextToARow",
            "WelcomeWindow_TheDotLabeled", "WelcomeWindow_ChipsBelowAnAccountClick",
        })
        {
            Assert.True(At(xaml, below) > heading, $"{below} only matters with several accounts and must sit under the heading.");
        }
    }

    private sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_CAPTURE_ROWS") != "1")
            {
                Skip = "Opt-in capture: set RORORO_CAPTURE_ROWS=1 to write the item-10 PNGs under docs/cycles/.../mocks/compare/.";
            }
        }
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public WelcomeTourTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [OptInFact]
    public void WriteTheTourCaptures()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any())
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var target = Path.Combine(dir!, "docs", "cycles", "2026-11-01-account-library", "mocks", "compare");
        Directory.CreateDirectory(target);

        foreach (var themeId in new[] { "brand", "flatline" })
        {
            var theme = LibraryPageFixtures.Themes().Single(t => t.Id == themeId);
            foreach (var culture in new string?[] { null, "de", "ru" })
            {
                var previous = TranslationSource.Instance.CurrentCulture;
                if (culture is not null) TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
                try
                {
                    var png = ThemedWindowRender.Inspect(theme, $"welcome capture [{themeId}/{culture}]",
                        () => new App.About.WelcomeWindow(),
                        root =>
                        {
                            var scroll = (ScrollViewer)ThemedWindowRender.Find(root, fe => fe is ScrollViewer, "tour scroll viewer");
                            _output.WriteLine($"{themeId}/{culture ?? "en"}: tips extent {scroll.ExtentHeight:F1} vs viewport {scroll.ViewportHeight:F1}");
                            var ground = (Brush)root.FindResource("BgBrush");
                            var visual = new DrawingVisual();
                            using (var dc = visual.RenderOpen())
                            {
                                dc.DrawRectangle(ground, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                                dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                                    null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                            }

                            var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                            bitmap.Render(visual);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = new MemoryStream();
                            encoder.Save(stream);
                            return stream.ToArray();
                        });
                    var name = $"item10-tour-{culture ?? "en"}-{themeId}-96dpi.png";
                    File.WriteAllBytes(Path.Combine(target, name), png);
                    _output.WriteLine(name);
                }
                finally
                {
                    TranslationSource.Instance.CurrentCulture = previous;
                }
            }
        }
    }
}
