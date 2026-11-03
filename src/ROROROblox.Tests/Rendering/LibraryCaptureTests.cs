using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.Localization;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Opt-in capture of the v1.36 Library page for the screenshot acceptance box of item 9: the C1 grid, the list
/// view, the empty Library and the D2 Play as flyout, in brand, midnight, magenta-heat and flatline, at 96 and
/// 120 DPI, plus German at 120 DPI (the language with the longest buttons). Writes <c>item9-*.png</c> under
/// <c>docs/cycles/2026-11-01-account-library/mocks/compare/</c>.
/// <para>
/// OFF BY DEFAULT: the normal suite must not write files into the repo. Set <c>RORORO_CAPTURE_ROWS=1</c>:
/// <c>$env:RORORO_CAPTURE_ROWS=1; dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter FullyQualifiedName~LibraryCaptureTests</c>.
/// Every game, server and account name is made up. As with the item-6 and item-7 captures, layout runs at 96 and the
/// bitmap is rendered at the requested DPI; a real 125% display re-runs layout and is not reproduced here. The flyout
/// is drawn inline (an open popup is a separate window this harness cannot render), so its disabled item's tooltip is
/// not in the picture.
/// </para>
/// </summary>
[Collection("MutatesUiCulture")]
public class LibraryCaptureTests
{
    private sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_CAPTURE_ROWS") != "1")
            {
                Skip = "Opt-in capture: set RORORO_CAPTURE_ROWS=1 to write the item-9 PNGs under docs/cycles/.../mocks/compare/.";
            }
        }
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public LibraryCaptureTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

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

    private static byte[] Render(Theme theme, double dpi, Func<Window> build, string? culture)
    {
        var previous = TranslationSource.Instance.CurrentCulture;
        if (culture is not null) TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
        try
        {
            return ThemedWindowRender.Inspect(
                theme,
                $"library capture [{theme.Id}] @{dpi}dpi",
                build,
                root =>
                {
                    var ground = root.TryFindResource("BgBrush") as Brush
                        ?? throw new InvalidOperationException("BgBrush did not resolve; refusing to paint a white PNG.");
                    var scale = dpi / 96.0;
                    var w = (int)Math.Round(root.ActualWidth * scale);
                    var h = (int)Math.Round(root.ActualHeight * scale);

                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(ground, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                        dc.DrawRectangle(
                            new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                            null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                    }

                    var bitmap = new RenderTargetBitmap(w, h, dpi, dpi, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    return stream.ToArray();
                });
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
        }
    }

    private void Write(string name, byte[] png)
    {
        File.WriteAllBytes(Path.Combine(CompareDir(), name), png);
        _output.WriteLine($"{name} ({png.Length} bytes)");
    }

    [OptInFact]
    public void WriteTheLibraryCaptures_InEveryBuiltInTheme_At96And120()
    {
        foreach (var theme in LibraryPageFixtures.Themes())
        {
            foreach (var dpi in new[] { 96d, 120d })
            {
                var tag = $"{theme.Id}-{(int)dpi}dpi";
                Write($"item9-grid-{tag}.png", Render(theme, dpi, () => LibraryPageFixtures.Build(LibraryScene.Showcase).Window, null));
                Write($"item9-list-{tag}.png", Render(theme, dpi, () => LibraryPageFixtures.Build(LibraryScene.Showcase, view: "list").Window, null));
                Write($"item9-empty-{tag}.png", Render(theme, dpi, () => LibraryPageFixtures.Build(LibraryScene.Empty).Window, null));
                Write($"item9-flyout-{tag}.png", Render(theme, dpi, () => LibraryPageFixtures.BuildFlyout(theme).Window, null));
            }
        }
    }

    [OptInFact]
    public void WriteTheGermanCaptures_At120()
    {
        // German has the longest button words ("Spielen als"): the case the tile's two buttons cost the most in.
        var brand = LibraryPageFixtures.Themes().Single(t => t.Id == "brand");
        Write("item9-grid-de-brand-120dpi.png", Render(brand, 120, () => LibraryPageFixtures.Build(LibraryScene.Showcase).Window, "de"));
        Write("item9-list-de-brand-120dpi.png", Render(brand, 120, () => LibraryPageFixtures.Build(LibraryScene.Showcase, view: "list").Window, "de"));
        Write("item9-empty-de-brand-120dpi.png", Render(brand, 120, () => LibraryPageFixtures.Build(LibraryScene.Empty).Window, "de"));
        Write("item9-flyout-de-brand-120dpi.png", Render(brand, 120, () => LibraryPageFixtures.BuildFlyout(brand).Window, "de"));
        // The chips as the C1 mock draws them: Recent and Favourites on, the check glyph beside each.
        foreach (var id in new[] { "brand", "flatline" })
        {
            var theme = LibraryPageFixtures.Themes().Single(t => t.Id == id);
            Write($"item9-grid-chips-{id}-96dpi.png", Render(theme, 96, () => LibraryPageFixtures.Build(LibraryScene.Showcase, filters: "Recent,Favourites").Window, null));
        }

        Write("item9-grid-longnames-brand-96dpi.png", Render(brand, 96, () => LibraryPageFixtures.Build(LibraryScene.LongNames).Window, null));
        Write("item9-grid-hundred-brand-96dpi.png", Render(brand, 96, () => LibraryPageFixtures.Build(LibraryScene.Hundred).Window, null));
    }
}
