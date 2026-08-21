using System.IO;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// Keeps the derived edge off decorative surfaces (wave 5).
/// <para>
/// <c>Divider</c> does two jobs: a separator between rows and around cards, where the author's
/// faint hairline is correct and intended, and the boundary of an interactive control, where WCAG
/// 1.4.11 requires 3:1. <c>InteractiveEdgeBrush</c> is derived to satisfy the second without
/// touching the first.
/// </para>
/// <para>
/// The first version of this wave's plan missed the distinction and would have replaced
/// <c>DividerBrush</c> app-wide — measured at <c>#1F3149 -> #707B8B</c> in brand since F-090 made
/// the derivation clear every surface rather than Navy alone (it read <c>#5E6B7C</c> before), which repaints
/// every row rule and card edge in every user's theme from a dark hairline to mid grey, to fix a
/// problem those surfaces do not have. The correction only holds while the derived brush stays on
/// controls, and "just use the visible one everywhere, it looks cleaner" is a very easy edit to
/// make. This test is what makes that edit fail loudly instead of shipping.
/// </para>
/// </summary>
public class InteractiveEdgeBindingTests
{
    /// <summary>
    /// Element types that USUALLY draw a surface rather than a control. "Usually" is doing real work
    /// here — see <see cref="IsInteractive"/>. A shape type alone does not settle the question.
    /// </summary>
    private static readonly string[] DecorativeElements =
        ["Border", "Rectangle", "Separator", "Line", "Ellipse", "Path", "Polygon"];

    /// <summary>
    /// Properties that paint an element's outline. <c>Stroke</c> and <c>Fill</c> are here because a
    /// <c>Rectangle</c> or <c>Path</c> draws its edge with those, not with <c>BorderBrush</c>.
    /// </summary>
    private static readonly string[] EdgeProperties = ["BorderBrush", "Stroke", "Fill", "Background"];

    [Fact]
    public void TheDerivedEdgeIsNeverBoundToADecorativeSurface()
    {
        var offenders = new List<string>();

        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            XDocument doc;
            try { doc = XDocument.Load(file.FullPath, LoadOptions.SetLineInfo); }
            catch (System.Xml.XmlException) { continue; }

            foreach (var el in doc.Descendants())
            {
                var name = el.Name.LocalName;

                // 1. A Style targeting a decorative type. THE HOLE THE REVIEW GATE FOUND: this test
                //    used to read attributes on elements only, so the single easiest version of the
                //    edit it exists to prevent — one Setter in ControlStyles.xaml with
                //    TargetType="Border" — repainted every Border in the app and scanned clean. The
                //    wave's own thesis is "hand-copies become named styles"; a style-blind fence
                //    guards the form the wave is moving away from.
                if (name == "Style"
                    && Strip((string?)el.Attribute("TargetType")) is { } targetType
                    && DecorativeElements.Contains(targetType))
                {
                    foreach (var setter in el.Descendants().Where(d => d.Name.LocalName == "Setter"))
                    {
                        var prop = (string?)setter.Attribute("Property");
                        var value = (string?)setter.Attribute("Value") ?? string.Join("", setter.Descendants().Select(d => d.Value));
                        if (prop is not null && EdgeProperties.Contains(prop) && Mentions(value))
                        {
                            offenders.Add($"{file.Label}{Line(el)}: <Style TargetType=\"{targetType}\"> sets {prop}");
                        }
                    }
                    continue;
                }

                // 2. Property-element syntax: <Border.BorderBrush>…</Border.BorderBrush>.
                if (name.Contains('.', StringComparison.Ordinal))
                {
                    var owner = name[..name.IndexOf('.', StringComparison.Ordinal)];
                    var prop = name[(name.IndexOf('.', StringComparison.Ordinal) + 1)..];
                    if (DecorativeElements.Contains(owner) && EdgeProperties.Contains(prop)
                        && Mentions(el.Value) && !XamlStyleScanner.IsInteractive(el.Parent ?? el))
                    {
                        offenders.Add($"{file.Label}{Line(el)}: <{name}> property element");
                    }
                    continue;
                }

                // 3. The direct-attribute form the original test caught.
                if (!DecorativeElements.Contains(name) || XamlStyleScanner.IsInteractive(el)) continue;

                foreach (var attr in el.Attributes().Where(a => Mentions(a.Value)))
                {
                    offenders.Add($"{file.Label}{Line(el)}: <{name} {attr.Name}=\"{attr.Value}\">");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"{ThemeSlotName} is for interactive control boundaries only. A separator or card edge "
            + "is not a UI component boundary — WCAG 1.4.11 does not govern it, and binding the "
            + "derived value there repaints every user's authored theme:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void NoControlBuiltInCodeBehindKeepsTheDecorativeBrushAsItsBoundary()
    {
        // THE GAP THIS CLOSES. Wave 5's sweep commit claimed "zero Buttons left on DividerBrush."
        // That was true of the markup and false of the app: CaptionColorPickerWindow and
        // SquadLaunchWindow each build a Button in C# and set its BorderBrush from FindResource.
        // Every other test in this wave parses XAML, so all of them agreed with the wrong claim.
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.NotNull(appDir);

        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(appDir!, "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;

            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("BorderBrush", StringComparison.Ordinal)) continue;
                if (!lines[i].Contains("DividerBrush", StringComparison.Ordinal)) continue;

                offenders.Add($"{Path.GetFileName(path)}:{i + 1}: {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A control's boundary built in code is still a control's boundary — WCAG 1.4.11 does "
            + $"not care which language drew it. Use {ThemeSlotName}:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheFenceSeesTheAppItClaimsTo()
    {
        // Every assertion above is a "found nothing" — which is also what a broken scan returns.
        // The app has ~30 XAML files, and the derived brush must actually appear somewhere or the
        // wave did not ship.
        var files = XamlStyleScanner.EnumerateAppXamlFiles().ToList();
        Assert.True(files.Count >= 20, $"Expected the app's XAML, found {files.Count} files.");

        var mentions = files.Count(f => File.ReadAllText(f.FullPath).Contains(ThemeSlotName, StringComparison.Ordinal));
        Assert.True(mentions >= 2, $"{ThemeSlotName} appears in {mentions} files — the fence has nothing to guard.");
    }

    private static string? Strip(string? targetType) =>
        targetType?.Replace("{x:Type ", "", StringComparison.Ordinal).TrimEnd('}').Split(':').Last();

    private static bool Mentions(string? value) =>
        value?.Contains(ThemeSlotName, StringComparison.Ordinal) == true;

    private static string Line(XElement el) =>
        el is System.Xml.IXmlLineInfo li && li.HasLineInfo() ? $":{li.LineNumber}" : "";

    [Fact]
    public void TheDerivedEdgeIsNotAThemeSlotAuthorsHaveToSupply()
    {
        // Invariant 6: Theme has exactly ten required slots and every user theme on disk supplies
        // all ten. If this ever becomes a record property, every existing theme file breaks.
        var props = typeof(ROROROblox.Core.Theming.Theme)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("InteractiveEdge", props);
        Assert.DoesNotContain("InteractiveEdgeBrush", props);
        Assert.Equal("InteractiveEdgeBrush", ROROROblox.Core.Theming.ThemeSlots.InteractiveEdge);
    }

    private const string ThemeSlotName = "InteractiveEdgeBrush";
}
