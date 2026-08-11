using System.Windows;
using System.Windows.Media;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// v1.19 theme feed, host side. Covers <c>ThemeService.ApplyTo</c>'s resolved-palette return and
/// the <c>ThemeApplied</c> raise. Nothing here constructs a <c>Window</c>: <c>ApplyTo</c> is static
/// and dictionary-taking precisely so a theme can be resolved with no <c>Application</c>, and the
/// feed's whole design leans on that.
/// </summary>
public class ThemeFeedTests
{
    private static Theme ThemeWith(string bg = "#0F1F31", string cyan = "#17D4FA", bool isBuiltIn = true) =>
        new(
            Id: "test", Name: "Test",
            Bg: bg, Cyan: cyan, Magenta: "#F22F89", White: "#FFFFFF",
            MutedText: "#9AA8B8", Divider: "#1F3149", RowBg: "#15263A",
            RowExpiredBg: "#3A2D14", RowExpiredAccent: "#F1B232", Navy: bg,
            IsBuiltIn: isBuiltIn);

    /// <summary>Every slot pre-seeded, mirroring App.xaml — ApplySlot replaces, it never creates.</summary>
    private static ResourceDictionary SeededDictionary(string seed = "#010203")
    {
        var d = new ResourceDictionary();
        foreach (var key in new[]
        {
            ThemeSlots.Bg, ThemeSlots.Cyan, ThemeSlots.Magenta, ThemeSlots.White,
            ThemeSlots.MutedText, ThemeSlots.Divider, ThemeSlots.RowBg,
            ThemeSlots.RowExpiredBg, ThemeSlots.RowExpiredAccent, ThemeSlots.Navy,
            ThemeSlots.InteractiveEdge,
        })
        {
            d[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(seed)!);
        }
        return d;
    }

    [Fact]
    public void ApplyTo_ReturnsAllElevenSlots_IncludingTheDerivedEdge()
    {
        var resources = SeededDictionary();

        var (_, palette) = ROROROblox.App.Theming.ThemeService.ApplyTo(
            resources, ThemeWith(), edgeAnswer: null);

        Assert.Equal("#0F1F31", palette.Bg);
        Assert.Equal("#17D4FA", palette.Cyan);
        Assert.Equal("#F22F89", palette.Magenta);
        Assert.Equal("#FFFFFF", palette.White);
        Assert.Equal("#9AA8B8", palette.MutedText);
        Assert.Equal("#1F3149", palette.Divider);
        Assert.Equal("#15263A", palette.RowBg);
        Assert.Equal("#3A2D14", palette.RowExpiredBg);
        Assert.Equal("#F1B232", palette.RowExpiredAccent);
        Assert.Equal("#0F1F31", palette.Navy);

        // The eleventh slot is the point. It is absent from Theme by design, so a consumer handed
        // only the record could not produce it without a second copy of EdgeRemediation.
        Assert.NotNull(palette.InteractiveEdge);
        Assert.NotEqual("#010203", palette.InteractiveEdge);
    }

    [Fact]
    public void ApplyTo_DerivedEdge_MatchesWhatEdgeRemediationResolved()
    {
        var theme = ThemeWith();
        var resources = SeededDictionary();

        var (decision, palette) = ROROROblox.App.Theming.ThemeService.ApplyTo(
            resources, theme, edgeAnswer: null);

        var expected = EdgeRemediation.Resolve(decision, theme.Navy, theme.Divider);
        Assert.Equal(expected.ToUpperInvariant(), palette.InteractiveEdge.ToUpperInvariant());
    }

    /// <summary>
    /// The test this whole design was chosen for.
    /// <para>
    /// <c>ApplySlot</c> returns early on a hex it cannot parse and leaves the previous brush in
    /// place, so the theme record and the screen disagree. A palette built by accumulating values
    /// as they are written would report <c>not-a-colour</c>; a palette read back out of the
    /// dictionary reports the seed that is genuinely still on screen.
    /// </para>
    /// <para>
    /// If this test can be made to pass by an accumulate-as-you-write implementation, the test is
    /// wrong rather than the design — it is the only thing standing between a plugin and a colour
    /// the host is not displaying.
    /// </para>
    /// </summary>
    [Fact]
    public void ApplyTo_UnparseableHex_ReportsTheBrushActuallyInPlace_NotTheRecord()
    {
        var resources = SeededDictionary("#010203");
        var broken = ThemeWith() with { Cyan = "not-a-colour" };

        var (_, palette) = ROROROblox.App.Theming.ThemeService.ApplyTo(
            resources, broken, edgeAnswer: null);

        Assert.Equal("#010203", palette.Cyan);
        Assert.NotEqual(broken.Cyan, palette.Cyan);

        // And the slots either side still took their new values, so this is a per-slot fallback
        // rather than the whole apply bailing out.
        Assert.Equal("#0F1F31", palette.Bg);
        Assert.Equal("#F22F89", palette.Magenta);
    }

    [Fact]
    public void ApplyTo_PaletteMatchesTheDictionaryItJustWrote()
    {
        var resources = SeededDictionary();

        var (_, palette) = ROROROblox.App.Theming.ThemeService.ApplyTo(
            resources, ThemeWith(bg: "#101010", cyan: "#D4D4D4"), edgeAnswer: null);

        static string Hex(ResourceDictionary d, string key)
        {
            var c = ((SolidColorBrush)d[key]).Color;
            return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        Assert.Equal(Hex(resources, ThemeSlots.Bg), palette.Bg);
        Assert.Equal(Hex(resources, ThemeSlots.Cyan), palette.Cyan);
        Assert.Equal(Hex(resources, ThemeSlots.InteractiveEdge), palette.InteractiveEdge);
    }
}
