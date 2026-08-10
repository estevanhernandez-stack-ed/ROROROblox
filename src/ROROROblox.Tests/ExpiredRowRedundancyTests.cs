using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

/// <summary>
/// The expired row and the compat banner say "expired" and "warning" in something other than
/// colour, in every theme (spec §6.2, §6.3).
/// <para>
/// WHAT THIS IS NOT. It does not render. Nothing in this repo renders — the suite is headless
/// xUnit with no STA thread and no dispatcher, so "an expired row is identifiable with
/// RowExpiredBg flattened to RowBg" is split into the two halves that CAN be checked without a
/// screen: the markup carries a rule that is not a fill, and the brush that rule resolves to
/// separates from the flattened row surface by a measured ratio under every shipped theme. Both
/// halves together are strong evidence and neither is a pixel. The pixel is owed to a human at
/// the capture round (spec §11.3 item 6), and this file does not stand in for it.
/// </para>
/// <para>
/// WHY A STRUCTURAL CLAUSE AT ALL. The rule's whole job is to survive a theme that takes the fill
/// away. A test that only measured colours would go green on a row that had quietly lost the rule
/// and kept the amber — which is the state the app shipped in before this change.
/// </para>
/// </summary>
public class ExpiredRowRedundancyTests
{
    private const string ExpiredAccentToken = "{DynamicResource RowExpiredAccentBrush}";

    /// <summary>The glyph both warning chips and the compat banner use. U+25B2, not emoji.</summary>
    private const string WarnGlyph = "▲";

    private static XDocument MainWindow()
    {
        var file = XamlStyleScanner.EnumerateAppXamlFiles()
            .FirstOrDefault(f => Path.GetFileName(f.FullPath) == "MainWindow.xaml");

        Assert.True(file.FullPath is not null,
            "MainWindow.xaml was not found by the XAML walk. Every clause below would pass "
            + "vacuously, so fail here instead.");

        return XDocument.Load(file.FullPath, LoadOptions.SetLineInfo);
    }

    /// <summary>
    /// A Style's own setters — not those belonging to a nested Style inside a Setter's Value.
    /// </summary>
    private static IEnumerable<XElement> OwnSetters(XElement style) =>
        style.Descendants()
            .Where(e => e.Name.LocalName == "Setter"
                        && e.Ancestors().First(a => a.Name.LocalName == "Style") == style);

    [Fact]
    public void TheExpiredRowCarriesA3pxRuleBoundToTheExpiredAccent()
    {
        // Same device Preferences' nav rail uses for selection — a 3px accent BAR, deliberately
        // not a fill, because a fill is the first thing a flattening theme deletes (F-002). One
        // vocabulary, two surfaces.
        var doc = MainWindow();

        var rules = doc.Descendants()
            .Where(e => e.Name.LocalName == "Rectangle")
            .Where(rect => rect.Descendants()
                .Where(t => t.Name.LocalName == "DataTrigger")
                .Any(t => t.Attribute("Binding")?.Value == "{Binding SessionExpired}"
                          && t.Attribute("Value")?.Value == "True"))
            .ToList();

        Assert.True(rules.Count == 1,
            $"Expected exactly one expired-row rule Rectangle in MainWindow.xaml, found {rules.Count}. "
            + "Zero means the non-colour carrier for an expired session is gone and the row is back "
            + "to saying it in amber only.");

        var rule = rules[0];

        Assert.Equal("3", rule.Attribute("Width")?.Value);
        Assert.Equal(ExpiredAccentToken, rule.Attribute("Fill")?.Value);

        // A DynamicResource, never a literal: a rule painted in a hardcoded amber would be the
        // same defect as the converters item 3 deleted, in a new shape.
        Assert.StartsWith("{DynamicResource ", rule.Attribute("Fill")?.Value);

        var style = rule.Descendants().Single(e => e.Name.LocalName == "Style");
        var setters = OwnSetters(style).ToList();

        // THE TRAP THIS CLAUSE EXISTS FOR. A local Visibility attribute on the Rectangle outranks
        // every Style trigger in WPF's value precedence, so the rule would compile, look correct
        // in the markup diff, and never once appear on screen. The default has to come from the
        // Style's own setter.
        Assert.True(rule.Attribute("Visibility") is null,
            "The rule Rectangle declares Visibility as a local attribute. A local value beats the "
            + "DataTrigger, so the rule would never show. Put the default in the Style's setter.");

        Assert.Contains(setters, s =>
            s.Attribute("Property")?.Value == "Visibility"
            && s.Attribute("Value")?.Value == "Hidden"
            && s.Parent == style);

        var shown = style.Descendants()
            .Single(e => e.Name.LocalName == "DataTrigger")
            .Descendants()
            .Single(e => e.Name.LocalName == "Setter");

        Assert.Equal("Visibility", shown.Attribute("Property")?.Value);
        Assert.Equal("Visible", shown.Attribute("Value")?.Value);
    }

    [Fact]
    public void TheRuleStaysLegibleWithTheExpiredBackgroundFlattenedIntoTheRowSurface()
    {
        // The acceptance question, asked as arithmetic: take the expired row's background away —
        // set RowExpiredBg to RowBg, which is exactly what a theme with nothing to say in colour
        // would do — and measure what is left. The rule is a graphical object carrying information
        // at that point, so WCAG 1.4.11's 3:1 is the bar, not 4.5:1.
        //
        // Measured 2026-08-10 through ThemeService.ApplyTo: brand 8.14:1, midnight 6.72:1,
        // magenta-heat 9.13:1, flatline 9.68:1. Headroom is wide enough that the 3:1 bar catches a
        // palette that genuinely broke rather than ordinary theme churn. Flatline's 9.68:1 is the
        // same number spec §5.3 records for the expired status dot, which is the cross-check that
        // this is measuring what it says it is.
        var measured = new List<string>();
        var failures = new List<string>();

        foreach (var theme in BuiltInThemes())
        {
            var flattened = theme with { RowExpiredBg = theme.RowBg };
            var resolved = Resolve(flattened);

            var surface = resolved["RowExpiredBgBrush"];
            var rule = resolved["RowExpiredAccentBrush"];

            // Parse health first: a null ratio means a hex did not resolve, which would make the
            // comparison below meaningless rather than false.
            var ratio = ContrastGuard.RatioBetween(surface, rule);
            Assert.True(ratio is not null,
                $"{theme.Id}: could not measure {rule} against {surface}. The theme is broken, not the rule.");

            Assert.Equal(resolved["RowBgBrush"], surface);   // the flattening actually took effect

            measured.Add($"{theme.Id} {ratio!.Value:F2}:1");
            if (ratio.Value < 3.0) failures.Add($"{theme.Id}: rule {rule} on flattened row {surface} = {ratio.Value:F2}:1");
        }

        Assert.True(failures.Count == 0,
            "With the expired background flattened into the ordinary row surface, the rule is the "
            + "only thing left saying 'expired' — and in these themes it does not clear 3:1:\n  "
            + string.Join("\n  ", failures)
            + "\nAll themes measured: " + string.Join(", ", measured));
    }

    [Fact]
    public void TheCompatBannerPrefixesTheSameWarnGlyph()
    {
        // One warning vocabulary across the window: the memory chip, the idle chip and this banner
        // all lead with the same glyph.
        var doc = MainWindow();

        var bound = doc.Descendants()
            .Where(e => e.Name.LocalName == "Run")
            .Where(r => r.Attribute("Text")?.Value == "{Binding RobloxCompatBanner}")
            .ToList();

        Assert.True(bound.Count == 1,
            $"Expected the compat banner's text to be one bound Run, found {bound.Count}.");

        var prefix = bound[0].ElementsBeforeSelf().LastOrDefault();
        Assert.True(prefix is not null && prefix.Name.LocalName == "Run",
            "The compat banner's bound text has no Run in front of it — the warn glyph is gone.");

        Assert.Equal(WarnGlyph + " ", prefix!.Attribute("Text")?.Value);

        // The glyph must never render on its own. The banner Border collapses on the same binding
        // that feeds the text, so an empty compat result takes the whole thing — glyph included —
        // off screen rather than leaving a bare triangle on the window.
        var banner = bound[0].Ancestors().First(a => a.Name.LocalName == "Border");
        Assert.Contains("RobloxCompatBanner", banner.Attribute("Visibility")?.Value ?? "");

        // Out of scope, and stated so a future reader does not read this file as covering it: the
        // Bloxstrap banner below this one holds literal #3F3000 / #8F7000 and takes no glyph this
        // cycle (spec §7). It gets a register row, not a prefix.
    }

    [Fact]
    public void TheWarnGlyphIsTheOneTheRegisterExempts()
    {
        // U+25B2 BLACK UP-POINTING TRIANGLE. Emoji_Presentation=No, so invariant 5's emoji rule
        // (which names only the two shipped exceptions) does not apply to it. Pinned by codepoint
        // because the failure mode here is an encoding accident, and mojibake still renders
        // something — a human proofing a screenshot can miss it, this cannot.
        Assert.Equal(0x25B2, (int)WarnGlyph[0]);
        Assert.Equal(System.Globalization.UnicodeCategory.OtherSymbol, char.GetUnicodeCategory(WarnGlyph[0]));

        var doc = MainWindow();
        var glyphsInMarkup = doc.Descendants()
            .SelectMany(e => e.Attributes())
            .Count(a => a.Value.Contains(WarnGlyph, StringComparison.Ordinal));

        Assert.True(glyphsInMarkup >= 1,
            "MainWindow.xaml carries no U+25B2 in any attribute. Either the banner prefix is gone "
            + "or the file was rewritten in a codepage that mangled it.");
    }

    /// <summary>
    /// The app's real built-in themes, pointed at a throwaway folder so user themes on a dev box
    /// cannot contaminate the result. Same construction <c>ContrastPairGateTests</c> uses; not
    /// shared, because a helper reaching across two gate files couples their failure modes.
    /// </summary>
    private static IReadOnlyList<Theme> BuiltInThemes()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-expired-rule-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn)
            .ToList();

        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
        catch (IOException) { }

        Assert.True(themes.Count >= 4,
            $"Expected the four built-in themes (brand, midnight, magenta-heat, flatline); got {themes.Count}.");

        return themes;
    }

    /// <summary>Resolves every theme slot to its #RRGGBB string through the app's own apply path.</summary>
    private static Dictionary<string, string> Resolve(Theme theme)
    {
        var resources = new ResourceDictionary();
        ROROROblox.App.Theming.ThemeService.ApplyTo(resources, theme, edgeAnswer: null);

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in resources.Keys)
        {
            if (key is string name && resources[key] is SolidColorBrush brush)
            {
                var c = brush.Color;
                resolved[name] = c.A == 255
                    ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                    : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
            }
        }

        return resolved;
    }
}
