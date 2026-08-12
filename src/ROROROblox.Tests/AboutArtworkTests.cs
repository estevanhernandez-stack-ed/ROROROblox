using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

/// <summary>
/// The About window's iso voxel stack is the MARK, and a themed logo is a broken logo. The plate it
/// sits on is chrome and follows the theme. This gate holds that line in both directions.
/// <para>
/// WHY BOTH DIRECTIONS. F-063 reads as "eight un-themed SolidColorBrush literals in AboutWindow",
/// and taken literally its fix recolours the 626 Labs mark. Spec §0.1 corrected it: the eight are
/// brand identity artwork in the same category as the per-account caption palette that
/// <c>ThemedStatusColourTests</c> already allow-lists — they paint WHO something is, not WHAT STATE
/// it is in. The real defect was two grounds, and those are now bound. So this file has to fail if
/// somebody themes the artwork AND if somebody freezes the grounds, because the row's own text
/// argues for the first and the next literal-sweep will argue for the second.
/// </para>
/// <para>
/// WHAT IT FOUND ON ITS FIRST RUN, 2026-08-11. The middle block's top face was
/// <c>{DynamicResource MagentaBrush}</c> — a theme slot inside the mark, shipped. It resolved to
/// <c>#F22F89</c> in brand and magenta-heat, <c>#C0407E</c> in midnight and <c>#6E6E6E</c> in
/// flatline, so the lit face of the magenta block recoloured while its two side faces stayed brand
/// magenta: a grey top on a magenta body under flatline. Neither the register row nor the cycle's
/// spec noticed it; spec §2 asserted the logo already rendered identically in all four themes,
/// which was the one thing about it that was not true. Item 4 rebound that face to
/// <c>MagentaDimBrush</c>, which is <c>#F22F89</c>, leaving brand and magenta-heat byte-identical.
/// </para>
/// </summary>
public class AboutArtworkTests
{
    /// <summary>
    /// The eight keyed brushes at <c>AboutWindow.xaml:13-20</c> that paint the mark. Named here
    /// rather than discovered, because "whatever is declared in Window.Resources" would silently
    /// absorb a ninth brush added later for something that is not artwork.
    /// </summary>
    private static readonly string[] ArtworkBrushes =
    [
        "CyanBrightBrush", "CyanDimBrush", "CyanShadowBrush",
        "MagentaDimBrush", "MagentaShadowBrush",
        "NavySoftBrush", "TealBrush", "TealDeepBrush",
    ];

    private static readonly Regex HexColour =
        new(@"^#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})$", RegexOptions.Compiled);

    /// <summary>
    /// THE CLAUSE THAT MATTERS. Every face of the mark paints from a fixed local brush, never
    /// through a theme slot. This is the headless form of "the logo renders byte-identical in all
    /// four themes": a <c>StaticResource</c> onto a literal-hex brush declared in this window cannot
    /// vary by theme, because <c>ThemeService</c> only ever replaces brush instances it owns by key.
    /// </summary>
    [Fact]
    public void NoFaceOfTheMarkPaintsFromAThemeSlot()
    {
        var canvas = LogoCanvas();
        var offenders = new List<string>();

        foreach (var face in canvas.Descendants().Where(e => e.Name.LocalName == "Polygon"))
        {
            var fill = face.Attribute("Fill")?.Value ?? "(unset)";

            if (fill.Contains("DynamicResource", StringComparison.Ordinal))
            {
                offenders.Add($"Polygon{Line(face)} Fill=\"{fill}\" — a theme slot inside the mark.");
                continue;
            }

            var key = ResourceKey(fill);
            if (key is null || !ArtworkBrushes.Contains(key))
            {
                offenders.Add($"Polygon{Line(face)} Fill=\"{fill}\" — not one of the eight artwork brushes.");
            }
        }

        Assert.True(canvas.Descendants().Count(e => e.Name.LocalName == "Polygon") >= 9,
            "Fewer than nine faces found in the logo Canvas. The mark is three stacked blocks of "
            + "three faces each; a lower count means this gate is measuring the wrong element.");

        Assert.True(offenders.Count == 0,
            "The 626 Labs mark must render identically under every theme, including ones users "
            + "write. A face bound to a theme slot recolours with the palette while the faces beside "
            + "it do not, which is how the magenta block shipped with a grey top under flatline. "
            + "The mark paints WHO this is; the theme paints WHAT STATE things are in. Use one of "
            + "the eight fixed artwork brushes:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The other direction. The Canvas is a GROUND for a fixed-colour mark, so it follows the theme
    /// — and it is bound rather than deleted on purpose. The faces are fixed hexes tuned for a dark
    /// field; on a light user theme a missing plate leaves the mark floating with no ground, and the
    /// four dark built-ins would never reveal it. F-063's own fix direction says "drop the canvas
    /// fill"; spec §2 overrode that with this reasoning, and this clause is where the override lives
    /// so a later reader does not quietly restore the row's version.
    /// </summary>
    [Fact]
    public void TheGroundUnderTheMarkFollowsTheTheme()
    {
        var fill = LogoCanvas().Attribute("Background")?.Value;

        Assert.True(fill is not null && fill.Contains("DynamicResource", StringComparison.Ordinal),
            $"The logo Canvas Background is \"{fill ?? "(unset)"}\". It must be a DynamicResource: "
            + "the plate is chrome, not the mark. Removing it entirely is also wrong — the mark's "
            + "faces are fixed hexes tuned for a dark field, and a light user theme would leave them "
            + "with no ground.");
    }

    /// <summary>
    /// The eight are literals, and none of their keys is a theme slot.
    /// <para>
    /// The second half is the sharper one. <c>ThemeService.ApplySlot</c> overwrites resources BY
    /// KEY, so an artwork brush that happened to be named <c>NavyBrush</c> would be repainted by
    /// every theme change no matter how many <c>StaticResource</c> references pointed at it. The
    /// window already declares <c>NavySoftBrush</c>, one character away from exactly that.
    /// </para>
    /// </summary>
    [Fact]
    public void TheArtworkBrushesAreFixedAndCannotCollideWithAThemeSlot()
    {
        var doc = AboutWindow();
        var slots = ThemeSlotNames();
        var problems = new List<string>();

        var declared = doc.Descendants()
            .Where(e => e.Name.LocalName == "SolidColorBrush")
            .Where(e => e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")) is not null)
            .ToDictionary(
                e => e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Value,
                e => e.Attribute("Color")?.Value ?? "(unset)",
                StringComparer.Ordinal);

        foreach (var key in ArtworkBrushes)
        {
            if (!declared.TryGetValue(key, out var colour))
            {
                problems.Add($"{key} is no longer declared in AboutWindow.xaml.");
                continue;
            }

            if (!HexColour.IsMatch(colour))
            {
                problems.Add($"{key} has Color=\"{colour}\", which is not a fixed hex.");
            }

            if (slots.Contains(key))
            {
                problems.Add($"{key} collides with a theme slot name — ThemeService.ApplySlot "
                    + "overwrites by key, so every StaticResource pointing at it would be repainted "
                    + "on any theme change.");
            }
        }

        Assert.True(problems.Count == 0,
            "The mark's eight brushes are fixed brand hexes with keys the theme system does not "
            + "own:\n  " + string.Join("\n  ", problems));
    }

    private static HashSet<string> ThemeSlotNames() =>
        typeof(ThemeSlots)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Extracts <c>Key</c> from <c>{StaticResource Key}</c>; null for anything else.</summary>
    private static string? ResourceKey(string markup)
    {
        var m = Regex.Match(markup, @"^\{StaticResource\s+([A-Za-z0-9_]+)\s*\}$");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// The 64x64 Canvas holding the mark, found by its size rather than by document order — a
    /// wrapper added around it later must not silently move this gate onto a different element.
    /// </summary>
    private static XElement LogoCanvas()
    {
        var matches = AboutWindow().Descendants()
            .Where(e => e.Name.LocalName == "Canvas")
            .Where(e => e.Attribute("Width")?.Value == "64" && e.Attribute("Height")?.Value == "64")
            .ToList();

        Assert.True(matches.Count == 1,
            $"Expected exactly one 64x64 Canvas in AboutWindow.xaml, found {matches.Count}.");

        return matches[0];
    }

    private static XDocument AboutWindow()
    {
        var file = XamlStyleScanner.EnumerateAppXamlFiles()
            .FirstOrDefault(f => Path.GetFileName(f.FullPath) == "AboutWindow.xaml");

        Assert.True(file.FullPath is not null,
            "AboutWindow.xaml was not found by the XAML walk — every clause here would pass vacuously.");

        return XDocument.Load(file.FullPath, LoadOptions.SetLineInfo);
    }

    private static string Line(XElement el) =>
        el is System.Xml.IXmlLineInfo li && li.HasLineInfo() ? $":{li.LineNumber}" : "";
}
