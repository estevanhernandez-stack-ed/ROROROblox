using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests;

/// <summary>
/// The two warning banners on the main window share ONE recipe, and the difference between them is
/// carried by text and the warn glyph rather than by hue (v1.21 item 1's ruling, recorded on
/// F-085).
/// <para>
/// WHY THIS EXISTS AS A SEPARATE GATE. Before v1.21 the Bloxstrap banner painted its own amber
/// —<c>#3F3000</c> / <c>#8F7000</c> / <c>#FFE3A6</c> — and a comment called that amber deliberate,
/// "distinct from the red-ish compat banner above it". Under <c>flatline</c> the compat banner goes
/// grey and the Bloxstrap one stayed amber, so the distinction the comment claimed was carried by
/// the defect: it survived only because one banner ignored the theme. Collapsing them onto one
/// recipe removes the distinction unless something else carries it, and this file is what keeps
/// that something in place.
/// </para>
/// <para>
/// WHAT IT DOES NOT DO. It does not render — see <c>ExpiredRowRedundancyTests</c>' note on the same
/// limitation. The structural half (no literal, both bound to the same two tokens) and the measured
/// half (the pair clears AA in every shipped theme) are both checkable headless; that the two
/// banners READ as two different warnings when stacked is owed to a human at the capture round.
/// </para>
/// </summary>
public class BannerRecipeTests
{
    /// <summary>
    /// The two banners, each identified by the binding that SHOWS it rather than by position, copy
    /// or Grid.Row. Position and copy are both things an edit may legitimately move; a banner's
    /// visibility binding is the closest thing it has to an identity.
    /// </summary>
    private static readonly (string Label, string VisibilityBinding)[] Banners =
    [
        ("compat", "RobloxCompatBanner"),
        ("bloxstrap", "BloxstrapWarningVisible"),
    ];

    private const string SurfaceToken = "RowExpiredBgBrush";
    private const string AccentToken = "RowExpiredAccentBrush";

    /// <summary>WCAG 1.4.3 AA for body text. Banner copy is prose, so 4.5 is the floor, not 3.0.</summary>
    private const double TextFloor = 4.5;

    /// <summary>Six or eight hex digits, guarded against XML character entities the same way
    /// <c>ThemedStatusColourTests</c> guards its own scan — <c>&amp;#x2630;</c> is markup, not a
    /// colour.</summary>
    private static readonly Regex LiteralColour =
        new(@"(?<![&\w])#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b", RegexOptions.Compiled);

    [Fact]
    public void NeitherBannerPaintsItselfWithALiteral()
    {
        var offenders = new List<string>();

        foreach (var (label, binding) in Banners)
        {
            var banner = FindBanner(binding);

            // The whole subtree, attributes and element text alike: a literal on the Border, on the
            // TextBlock inside it, or on a Run is the same defect wearing three different hats.
            foreach (var el in banner.DescendantsAndSelf())
            {
                foreach (var attr in el.Attributes())
                {
                    foreach (Match m in LiteralColour.Matches(attr.Value))
                    {
                        offenders.Add($"{label} banner <{el.Name.LocalName} {attr.Name}=\"…\">: {m.Value}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A warning banner painting itself with a hex is off the governed path — ThemeService "
            + "replaces brush instances in the resource dictionary and a literal in markup is not "
            + "one of them, so it survives every theme change unchanged. That is exactly how the "
            + "Bloxstrap banner came to be the one warm block left on the main window under a theme "
            + "whose entire argument is that no hue carries meaning. Bind the recipe:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void BothBannersBindTheSameRecipe()
    {
        var failures = new List<string>();

        foreach (var (label, binding) in Banners)
        {
            var banner = FindBanner(binding);

            Check(label, "Background", banner.Attribute("Background")?.Value, SurfaceToken);
            Check(label, "BorderBrush", banner.Attribute("BorderBrush")?.Value, AccentToken);

            // The banner's prose. Both banners put their text in a single TextBlock inside the
            // Border; the accent doubles as the text colour, which is what makes the 4.5 clause
            // below the one that matters rather than a 3.0 boundary check.
            var text = banner.Descendants().FirstOrDefault(e => e.Name.LocalName == "TextBlock");
            if (text is null)
            {
                failures.Add($"{label} banner has no TextBlock — cannot check its foreground.");
                continue;
            }

            Check(label, "TextBlock.Foreground", text.Attribute("Foreground")?.Value, AccentToken);

            void Check(string who, string property, string? actual, string expectedToken)
            {
                if (actual is null || !actual.Contains(expectedToken, StringComparison.Ordinal))
                {
                    failures.Add($"{who} banner {property} is \"{actual ?? "(unset)"}\", expected a "
                        + $"DynamicResource on {expectedToken}.");
                }
            }
        }

        Assert.True(failures.Count == 0,
            "Both warning banners take ONE themed recipe, and what tells them apart is their text "
            + "plus the warn glyph — F-032's precedent, where MutedText vs White measured 1.00:1 "
            + "under flatline so colour could not carry the distinction and weight took over. A "
            + "banner that drifts off the recipe re-opens the question this ruling closed:\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// The measured half. Banner text is the accent ON the banner surface, so this is a text pair
    /// and 4.5 is its floor. Measured before the migration rather than after — all four cleared,
    /// which is why item 2 was a straight rebind with no foreground change. Had one failed, the
    /// ruling was that the foreground moves, not the floor.
    /// </summary>
    [Fact]
    public void TheBannerRecipeClearsAaInEveryShippedTheme()
    {
        var failures = new List<string>();
        var measured = new List<string>();

        foreach (var theme in BuiltInThemes())
        {
            var resolved = Resolve(theme);
            var surface = resolved[SurfaceToken];
            var accent = resolved[AccentToken];

            var ratio = ContrastGuard.RatioBetween(surface, accent);
            Assert.True(ratio is not null,
                $"{theme.Id}: could not measure {accent} on {surface}. The theme is broken, not the recipe.");

            measured.Add($"{theme.Id} {ratio!.Value:F2}:1");
            if (ratio.Value < TextFloor)
            {
                failures.Add($"{theme.Id}: banner text {accent} on banner surface {surface} = "
                    + $"{ratio.Value:F2}:1, under {TextFloor}.");
            }
        }

        Assert.True(failures.Count == 0,
            "Both banners now carry their message in the accent colour on the expired surface, so "
            + "this pair IS the banner copy. Under AA it has to clear 4.5:1 in every theme the app "
            + "ships. Change the foreground, not the floor:\n  " + string.Join("\n  ", failures)
            + "\nAll themes measured: " + string.Join(", ", measured));
    }

    private static XElement FindBanner(string visibilityBinding)
    {
        var doc = MainWindow();

        var matches = doc.Descendants()
            .Where(e => e.Name.LocalName == "Border")
            .Where(b => (b.Attribute("Visibility")?.Value ?? "")
                .Contains(visibilityBinding, StringComparison.Ordinal))
            .ToList();

        Assert.True(matches.Count == 1,
            $"Expected exactly one Border shown by {visibilityBinding}, found {matches.Count}. "
            + "This gate identifies a banner by its visibility binding; if the banner was "
            + "restructured, fix the identification rather than loosening it.");

        return matches[0];
    }

    private static XDocument MainWindow()
    {
        var file = XamlStyleScanner.EnumerateAppXamlFiles()
            .FirstOrDefault(f => Path.GetFileName(f.FullPath) == "MainWindow.xaml");

        Assert.True(file.FullPath is not null,
            "MainWindow.xaml was not found by the XAML walk. Every clause in this file would pass "
            + "vacuously without it.");

        return XDocument.Load(file.FullPath, LoadOptions.SetLineInfo);
    }

    /// <summary>
    /// The app's real built-in themes, pointed at a throwaway folder so user themes on a dev box
    /// cannot contaminate the result. Same construction the other gate files use; deliberately not
    /// shared, because a helper reaching across gate files couples their failure modes.
    /// </summary>
    private static IReadOnlyList<Theme> BuiltInThemes()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-banner-recipe-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn)
            .ToList();

        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
        catch (IOException) { }

        Assert.True(themes.Count >= 4,
            $"Expected the four built-in themes (brand, midnight, magenta-heat, flatline); got {themes.Count}.");

        return themes;
    }

    private static Dictionary<string, string> Resolve(Theme theme)
    {
        var resources = new ResourceDictionary();
        ROROROblox.App.Theming.ThemeService.ApplyTo(resources, theme, edgeAnswer: null);

        var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in resources.Keys)
        {
            if (key is string name && resources[key] is System.Windows.Media.SolidColorBrush brush)
            {
                var c = brush.Color;
                resolved[name] = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            }
        }

        return resolved;
    }
}
