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
/// <c>DividerBrush</c> app-wide — measured at <c>#1F3149 -> #5E6B7C</c> in brand, which repaints
/// every row rule and card edge in every user's theme from a dark hairline to mid grey, to fix a
/// problem those surfaces do not have. The correction only holds while the derived brush stays on
/// controls, and "just use the visible one everywhere, it looks cleaner" is a very easy edit to
/// make. This test is what makes that edit fail loudly instead of shipping.
/// </para>
/// </summary>
public class InteractiveEdgeBindingTests
{
    /// <summary>Elements that draw a surface, not a control. A boundary here is decoration.</summary>
    private static readonly string[] DecorativeElements = ["Border", "Rectangle", "Separator", "Line", "Ellipse"];

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
                if (!DecorativeElements.Contains(el.Name.LocalName)) continue;

                foreach (var attr in el.Attributes())
                {
                    if (!attr.Value.Contains(ThemeSlotName, StringComparison.Ordinal)) continue;

                    var line = el is System.Xml.IXmlLineInfo li && li.HasLineInfo() ? $":{li.LineNumber}" : "";
                    offenders.Add($"{file.Label}{line}: <{el.Name.LocalName} {attr.Name}=\"{attr.Value}\">");
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
