using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ROROROblox.App.Localization;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Opt-in capture of the v1.36 account panel (B1, three columns) for the screenshot acceptance box of item 7:
/// the account row with its panel open and all three groups populated, the three empty states, and the whole
/// <c>MainWindow</c> at the declared 900x600 with one account, in brand, midnight, magenta-heat and flatline,
/// at 96 and 120 DPI, plus German at 120 DPI (the language B1's narrow columns cost the most in). Writes
/// <c>item7-*.png</c> under <c>docs/cycles/2026-11-01-account-library/mocks/compare/</c>. The C2 rework
/// (2026-11-02: coloured facts, the expander before the name, three per column with See all) writes
/// <c>c2-*.png</c> to the same folder, in brand and flatline.
/// <para>
/// OFF BY DEFAULT: the normal suite must not write files into the repo. Set <c>RORORO_CAPTURE_ROWS=1</c>:
/// <c>$env:RORORO_CAPTURE_ROWS=1; dotnet test src/ROROROblox.Tests/ -c Release --no-build --filter FullyQualifiedName~AccountPanelCaptureTests</c>.
/// Every account, game and server name is made up. Like the item-6 capture, layout runs at 96 and the bitmap is
/// rendered at the requested DPI; a real 125% display re-runs layout and is not reproduced here.
/// </para>
/// </summary>
public class AccountPanelCaptureTests
{
    private sealed class OptInFactAttribute : FactAttribute
    {
        public OptInFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("RORORO_CAPTURE_ROWS") != "1")
            {
                Skip = "Opt-in capture: set RORORO_CAPTURE_ROWS=1 to write the item-7 PNGs under docs/cycles/.../mocks/compare/.";
            }
        }
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public AccountPanelCaptureTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

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

    private static IReadOnlyList<Theme> Themes()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-panel-capture-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn && t.Id is "brand" or "midnight" or "magenta-heat" or "flatline").ToList();
        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); } catch (IOException) { }
        Assert.Equal(4, themes.Count);
        return themes;
    }

    /// <summary>Renders either the whole roster-plus-chrome content (<paramref name="wholeWindow"/>) or just the roster.</summary>
    private static byte[] Render(
        Theme theme, double dpi, PanelContent content, bool wholeWindow, string? culture, string accountName = "NovaFake",
        int accounts = 1, bool plus = false)
    {
        var previous = TranslationSource.Instance.CurrentCulture;
        if (culture is not null) TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
        try
        {
            return ThemedWindowRender.Inspect(
                theme,
                $"account panel [{theme.Id}] @{dpi}dpi",
                () => PanelWindowFixtures.Build(
                    accounts: accounts, expanded: true, content: content, savedGames: true, firstName: accountName,
                    note: content == PanelContent.Empty ? null : "Farm alt, keep at 20 FPS. Clan bank on Fridays.", plus: plus),
                root =>
                {
                    FrameworkElement target = root;
                    if (!wholeWindow)
                    {
                        var items = ThemedWindowRender.Find(root, fe => fe.Name == "AccountsItemsControl", "AccountsItemsControl");
                        target = items;
                    }

                    var ground = root.TryFindResource("BgBrush") as Brush
                        ?? throw new InvalidOperationException("BgBrush did not resolve; refusing to paint a white PNG.");
                    var scale = dpi / 96.0;
                    var w = (int)Math.Round(target.ActualWidth * scale);
                    var h = (int)Math.Round(target.ActualHeight * scale);

                    var visual = new DrawingVisual();
                    using (var dc = visual.RenderOpen())
                    {
                        dc.DrawRectangle(ground, null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                        dc.DrawRectangle(
                            new VisualBrush(target) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                            null, new Rect(0, 0, target.ActualWidth, target.ActualHeight));
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
        var path = Path.Combine(CompareDir(), name);
        File.WriteAllBytes(path, png);
        _output.WriteLine($"{name} ({png.Length} bytes)");
    }

    [OptInFact]
    public void WriteThePanelCaptures_InEveryBuiltInTheme_At96And120()
    {
        foreach (var theme in Themes())
        {
            foreach (var dpi in new[] { 96d, 120d })
            {
                var tag = $"{theme.Id}-{(int)dpi}dpi";
                Write($"item7-panel-full-{tag}.png", Render(theme, dpi, PanelContent.Full, wholeWindow: false, culture: null));
                Write($"item7-panel-empty-{tag}.png", Render(theme, dpi, PanelContent.Empty, wholeWindow: false, culture: null));
                Write($"item7-window-one-account-{tag}.png", Render(theme, dpi, PanelContent.Full, wholeWindow: true, culture: null));
            }
        }
    }

    /// <summary>
    /// The C2 rework, for the comparison against <c>mocks/c2-brand.png</c> (sections F2, E1 and P1): the account row with
    /// its facts line (Robux, Plus, friends online, last played, the note) and an open panel holding a HEAVY library (so the
    /// headers carry their totals and See all), and a second closed row under it (E1's closed expander); then the whole
    /// 900x600 window. Brand and flatline, 96 DPI.
    /// </summary>
    [OptInFact]
    public void WriteTheC2Captures_BrandAndFlatline()
    {
        foreach (var theme in Themes().Where(t => t.Id is "brand" or "flatline"))
        {
            Write($"c2-row-and-panel-{theme.Id}-96dpi.png",
                Render(theme, 96, PanelContent.Heavy, wholeWindow: false, culture: null, accounts: 2, plus: true));
            Write($"c2-window-{theme.Id}-96dpi.png",
                Render(theme, 96, PanelContent.Heavy, wholeWindow: true, culture: null, accounts: 2, plus: true));
        }
    }

    [OptInFact]
    public void WriteTheGermanCaptures_At120()
    {
        // B1's known cost: the columns get narrow in German at 125%. The names are the mock's, made up; the
        // fixture's strings are what the six catalogs say.
        var brand = Themes().Single(t => t.Id == "brand");
        Write("item7-panel-full-de-brand-120dpi.png",
            Render(brand, 120, PanelContent.Full, wholeWindow: false, culture: "de", accountName: "NovaFake"));
        Write("item7-panel-empty-de-brand-120dpi.png",
            Render(brand, 120, PanelContent.Empty, wholeWindow: false, culture: "de", accountName: "NovaFake"));
        // Names longer than any column: they trim with an ellipsis, beside their tag and beside Save.
        Write("item7-panel-longnames-de-brand-120dpi.png",
            Render(brand, 120, PanelContent.LongNames, wholeWindow: false, culture: "de", accountName: "NovaFake"));
        Write("item7-panel-longnames-brand-96dpi.png",
            Render(brand, 96, PanelContent.LongNames, wholeWindow: false, culture: null, accountName: "NovaFake"));
    }
}
