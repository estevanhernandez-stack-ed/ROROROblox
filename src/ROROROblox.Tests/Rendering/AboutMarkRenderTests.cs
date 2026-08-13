using System.IO;
using System.Windows.Controls;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// The 626 Labs mark in the About window renders with the SAME pixels under every built-in theme,
/// measured off a real <c>AboutWindow</c>, while the plate beneath it follows the theme.
/// <para>
/// WHY THIS EXISTS WHEN <c>AboutArtworkTests</c> ALREADY GUARDS THE MARK. That gate reads markup: no
/// polygon face may bind a theme slot, and the eight artwork brushes must be fixed hexes with keys
/// the theme system does not own. Strong, and still only evidence about markup — F-098's whole
/// lesson. It cannot see a template overriding a fill, a <c>DynamicResource</c> failing to resolve
/// and falling back silently, or a brush the theme reaches by some other route. This renders the
/// window and counts pixels, which is the only thing that can contradict a markup reading.
/// </para>
/// <para>
/// IT ALSO RETIRES AN EYES-ON ITEM. v1.21 closed owing a human "open About in all four themes, the
/// mark must look the same in each". A person comparing four screenshots would catch a face going
/// grey. They would not catch one channel drifting by one step. This counts every pixel.
/// </para>
/// <para>
/// WHAT THE SPIKE CORRECTED IN THIS FILE'S OWN FIRST DRAFT. It hashed the whole 64x64 region and
/// asserted the hash was identical across themes. That can never pass, and it is not the ruling:
/// item 4 bound the Canvas ground to <c>RowBgBrush</c> ON PURPOSE so a light user theme still gives
/// the fixed-colour faces something to sit on. The plate is INSIDE the region and is meant to
/// differ. The assertion has to separate the mark's own pixels from the ground they sit on, which
/// is what <see cref="MarkPixels"/> does.
/// </para>
/// </summary>
public class AboutMarkRenderTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public AboutMarkRenderTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The eight fixed brushes at <c>AboutWindow.xaml:13-20</c>. Named rather than discovered,
    /// matching <c>AboutArtworkTests</c>' list, so the two gates cannot drift apart silently.
    /// <para>
    /// <b>SEVEN of the eight actually paint, and the spike is how that was noticed.</b>
    /// <c>NavySoftBrush</c> (<c>#0F1F31</c>) was the Canvas ground until v1.21 item 4 bound that
    /// ground to <c>RowBgBrush</c> so it would follow the theme. Nothing references it now — the
    /// declaration is live, its use is gone. Kept in this list deliberately: if a face is ever
    /// rebound to it the count comparison should see that, and dropping it here would make the list
    /// disagree with <c>AboutArtworkTests</c>. The dead declaration itself wants a register row, not
    /// a silent delete inside a rendering spike.
    /// </para>
    /// </summary>
    private static readonly string[] MarkColours =
        ["#6CEAFD", "#12BFE3", "#0D94B8", "#F22F89", "#B81F66", "#0F1F31", "#2EE6C9", "#1A9F8B"];

    /// <summary>The mark's own Canvas, identified by the 64x64 size its markup declares.</summary>
    private static RegionSample MarkUnder(Theme theme, double dpi = 96) =>
        ThemedWindowRender.MeasureRegion(
            theme,
            $"AboutWindow mark [{theme.Id}] @{dpi}dpi",
            () => new ROROROblox.App.About.AboutWindow(),
            content => ThemedWindowRender.Find(
                content,
                fe => fe is Canvas { Width: 64, Height: 64 },
                "the 64x64 logo Canvas"),
            dpi);

    /// <summary>
    /// The mark's pixels: every colour in the region that is one of the eight fixed artwork brushes,
    /// with its exact pixel count. Excludes the themed plate and antialiasing blends between the two,
    /// because a blend of a fixed face against a themed ground is legitimately theme-dependent.
    /// </summary>
    private static SortedDictionary<string, int> MarkPixels(RegionSample sample) =>
        new(sample.Histogram
            .Where(h => MarkColours.Contains(h.Colour, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(h => h.Colour.ToUpperInvariant(), h => h.Count),
            StringComparer.Ordinal);

    [Fact]
    public void TheMarkPaintsTheSamePixelsUnderEveryBuiltInTheme()
    {
        var perTheme = new Dictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);

        foreach (var theme in BuiltInThemes())
        {
            var sample = MarkUnder(theme);
            var mark = MarkPixels(sample);
            perTheme[theme.Id] = mark;

            _output.WriteLine($"{theme.Id,-14} {sample.Width}x{sample.Height}  mark pixels: "
                + string.Join(" ", mark.Select(kv => $"{kv.Key}x{kv.Value}")));
        }

        // Vacuity floor. "All themes agree" is also true of zero faces found, which is what a
        // misaligned region or an unresolved brush produces.
        foreach (var (id, mark) in perTheme)
        {
            Assert.True(mark.Count >= 6,
                $"Theme '{id}' matched only {mark.Count} of the eight artwork colours in the "
                + "rendered mark. The region is misaligned, or the faces are not painting — either "
                + "way the comparison below would agree about nothing.");
        }

        var reference = perTheme.First();
        var divergent = perTheme
            .Where(kv => !kv.Value.SequenceEqual(reference.Value))
            .Select(kv => $"{kv.Key,-14} {string.Join(" ", kv.Value.Select(c => $"{c.Key}x{c.Value}"))}")
            .ToList();

        Assert.True(divergent.Count == 0,
            "The 626 Labs mark painted DIFFERENT pixels under different themes. It is brand identity "
            + "artwork — it paints WHO this product is, not WHAT STATE something is in, and a themed "
            + "logo is a broken logo. This is the pixel form of what AboutArtworkTests asserts in "
            + "markup; if that gate is green and this one is not, the theme is reaching the mark by a "
            + "route markup cannot show.\n  "
            + $"{reference.Key,-14} {string.Join(" ", reference.Value.Select(c => $"{c.Key}x{c.Value}"))}  (reference)\n  "
            + string.Join("\n  ", divergent));
    }

    /// <summary>
    /// The other half of item 4's ruling, and the clause that stops the one above passing for the
    /// wrong reason: a mark on a plate that never changed would agree across themes trivially.
    /// </summary>
    [Fact]
    public void ThePlateBeneathTheMarkFollowsTheTheme()
    {
        var plates = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var theme in BuiltInThemes())
        {
            var sample = MarkUnder(theme);
            var expected = theme.RowBg.ToUpperInvariant();
            plates[theme.Id] = expected;

            var painted = sample.Histogram
                .FirstOrDefault(h => string.Equals(h.Colour, expected, StringComparison.OrdinalIgnoreCase));

            Assert.True(painted.Count > 0,
                $"Theme '{theme.Id}': the plate colour {expected} (this theme's RowBg) does not "
                + "appear in the rendered mark region at all. The Canvas ground is bound to "
                + "RowBgBrush deliberately — removing it would leave the fixed-colour faces with no "
                + "ground on a light user theme.\n" + sample.Describe());
        }

        Assert.True(plates.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 3,
            "Fewer than three distinct plate colours across four themes, so the identity clause "
            + "above may be passing because nothing varies rather than because the mark is fixed: "
            + string.Join(", ", plates.Select(kv => $"{kv.Key}={kv.Value}")));
    }

    /// <summary>
    /// The shared-host trade, measured instead of trusted. <c>WindowRenderHost</c> gives up
    /// <c>Sta</c>'s fresh-thread-per-call isolation because <see cref="System.Windows.Application"/>
    /// is one-per-AppDomain and has thread affinity — and <c>Sta</c>'s own doc is right that reusing
    /// a thread across themes is how one theme's resolution leaks into the next AS A PASS.
    /// <para>
    /// So: render brand, then midnight, then brand again. If anything from the midnight render
    /// survives into the third, the two brand results differ. This is the clause that makes the
    /// shared host defensible; if it ever fails, the host is wrong, not this test.
    /// </para>
    /// </summary>
    [Fact]
    public void RenderingIsNotContaminatedByThePreviousTheme()
    {
        var themes = BuiltInThemes();
        var brand = themes.Single(t => t.Id == "brand");
        var midnight = themes.Single(t => t.Id == "midnight");

        var first = MarkUnder(brand);
        var interleaved = MarkUnder(midnight);
        var second = MarkUnder(brand);

        _output.WriteLine($"brand#1   {first.Hash}");
        _output.WriteLine($"midnight  {interleaved.Hash}");
        _output.WriteLine($"brand#2   {second.Hash}");

        Assert.True(first.Hash == second.Hash,
            "Rendering brand, then midnight, then brand produced two DIFFERENT brand results, so "
            + "state is leaking between renders on the shared host thread. Sta uses a fresh STA "
            + "thread per call precisely to prevent this; WindowRenderHost cannot, because "
            + "Application is one-per-AppDomain with thread affinity. This clause is the evidence "
            + "that trade is safe, and it just stopped being safe.\n"
            + $"  brand #1 {first.Hash}\n  brand #2 {second.Hash}");

        Assert.True(first.Hash != interleaved.Hash,
            "brand and midnight rendered byte-identical marks INCLUDING the plate, which means the "
            + "theme is not reaching the window at all and every clause in this file is vacuous.");
    }

    /// <summary>
    /// The same claim at fractional scaling. Two of this project's geometry defects existed only at
    /// 125% — an inset that rounds evenly at 96 splits unevenly at 120 — so a gate that only renders
    /// at 96 cannot see the bug a user reports on a scaled display.
    /// </summary>
    [Fact]
    public void TheMarkPaintsTheSamePixelsAcrossThemesAtFractionalScaling()
    {
        var perTheme = BuiltInThemes()
            .ToDictionary(t => t.Id, t => MarkPixels(MarkUnder(t, dpi: 120)), StringComparer.Ordinal);

        foreach (var (id, mark) in perTheme)
        {
            _output.WriteLine($"{id,-14} @120dpi  " + string.Join(" ", mark.Select(kv => $"{kv.Key}x{kv.Value}")));
            Assert.True(mark.Count >= 6,
                $"Theme '{id}' matched only {mark.Count} artwork colours at 120 DPI.");
        }

        var reference = perTheme.First();
        var divergent = perTheme.Where(kv => !kv.Value.SequenceEqual(reference.Value)).Select(kv => kv.Key).ToList();

        Assert.True(divergent.Count == 0,
            "The mark diverges across themes at 120 DPI even if it holds at 96. Themes are: "
            + string.Join(", ", divergent));
    }

    private static IReadOnlyList<Theme> BuiltInThemes()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-about-mark-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn)
            .ToList();

        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
        catch (IOException) { }

        Assert.True(themes.Count >= 4, $"Expected the four built-in themes; got {themes.Count}.");
        return themes;
    }
}
