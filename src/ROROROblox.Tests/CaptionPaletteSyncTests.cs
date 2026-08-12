using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The caption palette exists twice, and until this test the only thing holding the copies together
/// was a comment.
/// <para>
/// <c>Converters.cs</c> writes it as <c>Color.FromRgb(0x1E, 0x40, 0xAF)</c> triples for the WPF
/// swatch in Settings; <c>Tray/RobloxWindowDecorator.cs</c> writes it as <c>uint</c> ARGB
/// <c>0xFF1E40AF</c> for <c>DwmSetWindowAttribute</c>, which tints the *Roblox client's* title bar
/// so a player can tell eight running accounts apart. Two representations because two APIs; the
/// values have to agree or the swatch in Settings advertises a colour the window does not use.
/// </para>
/// <para>
/// <b>Why this is a test and not a comment.</b> The comment — "keep in sync if either changes" — is
/// a real rule with no instrument, which F-098 found this repo has now been bitten by four times.
/// The aggravating detail here is the two literal FORMATS: a drift between
/// <c>Color.FromRgb(0x07, 0x58, 0x85)</c> and <c>0xFF075985</c> does not look like a drift to
/// anyone diffing either file on its own.
/// </para>
/// <para>
/// These colours are deliberately NOT theme-derived and no theming gate should ever count them —
/// they paint a window this app does not own. That is also why they sat outside every instrument in
/// the suite until the audit went looking.
/// </para>
/// </summary>
public class CaptionPaletteSyncTests
{
    private static string Read(string relative)
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "src", "ROROROblox.App", relative);
        Assert.True(File.Exists(path), $"expected {relative} at {path}");
        return File.ReadAllText(path);
    }

    /// <summary>The WPF side: <c>Color.FromRgb(0xRR, 0xGG, 0xBB)</c> → <c>RRGGBB</c>.</summary>
    private static List<string> ConverterColours()
    {
        var text = Read("Converters.cs");
        return Regex.Matches(text, @"Color\.FromRgb\(\s*0x([0-9A-Fa-f]{2})\s*,\s*0x([0-9A-Fa-f]{2})\s*,\s*0x([0-9A-Fa-f]{2})\s*\)")
            .Select(m => (m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value).ToUpperInvariant())
            .ToList();
    }

    /// <summary>
    /// The Win32 side: <c>0xFFRRGGBB</c> → <c>RRGGBB</c>, alpha dropped.
    /// <para>
    /// Sliced to the <c>AutoPalette</c> array and the <c>MainCaptionColor</c> const rather than
    /// scanning the whole file. That file is Win32 interop and carries other hex constants; a
    /// file-wide sweep would pick one up the day somebody adds it and fail for a reason that has
    /// nothing to do with the palette. A gate that cries wolf gets muted, which is a slower version
    /// of the same failure this one exists to catch.
    /// </para>
    /// </summary>
    private static List<string> DecoratorColours()
    {
        var text = Read(Path.Combine("Tray", "RobloxWindowDecorator.cs"));

        var start = text.IndexOf("AutoPalette", StringComparison.Ordinal);
        Assert.True(start >= 0, "RobloxWindowDecorator.cs no longer declares AutoPalette.");

        var mainIdx = text.IndexOf("MainCaptionColor", StringComparison.Ordinal);
        Assert.True(mainIdx > start, "RobloxWindowDecorator.cs no longer declares MainCaptionColor after AutoPalette.");

        var end = text.IndexOf(';', mainIdx);
        Assert.True(end > mainIdx, "MainCaptionColor's declaration is not terminated.");

        return Regex.Matches(text[start..end], @"0xFF([0-9A-Fa-f]{6})\b")
            .Select(m => m.Groups[1].Value.ToUpperInvariant())
            .ToList();
    }

    [Fact]
    public void TheTwoCopiesOfTheAutoPaletteAgree()
    {
        var wpf = ConverterColours();
        var win32 = DecoratorColours();

        // Vacuity floor first. Both readers are regexes over source text, so a rename or a
        // reformat that broke either pattern would leave two empty lists comparing equal — the
        // exact way a scan reports agreement by seeing nothing, which is what F-098 is about.
        Assert.True(wpf.Count == 9,
            $"expected 8 auto-palette colours plus the main-account magenta in Converters.cs, found {wpf.Count}. "
            + "The pattern broke, or the palette changed shape.");
        Assert.True(win32.Count == 9,
            $"expected 8 auto-palette colours plus the main-account magenta in RobloxWindowDecorator.cs, found {win32.Count}. "
            + "The pattern broke, or the palette changed shape.");

        Assert.True(wpf.SequenceEqual(win32, StringComparer.Ordinal),
            "The caption palette's two copies have drifted.\n"
            + $"  Converters.cs            : {string.Join(", ", wpf)}\n"
            + $"  RobloxWindowDecorator.cs : {string.Join(", ", win32)}\n"
            + "They are written in different literal formats, so this will not look like a drift in "
            + "either file on its own. Settings would advertise a colour the title bar does not use.");
    }
}
