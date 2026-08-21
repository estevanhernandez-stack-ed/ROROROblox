using System.IO;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// F-073 — a dismiss action must not be announced before the thing it dismisses.
/// <para>
/// Screen readers and the Tab key both follow DECLARATION order, not visual order. Both of the main
/// window's banners used a <c>DockPanel</c>, which forced the button to be declared first so it
/// could dock right — so the automation tree offered "Dismiss the frame-rate cap warning" and only
/// then the warning. A sighted user saw nothing wrong, which is exactly why it survived: the defect
/// is invisible in the medium most of us check in.
/// </para>
/// <para>
/// The fix was a <c>Grid</c>, because a Grid decouples declaration order from visual position and a
/// DockPanel cannot. Verified live through UI Automation after the change: the banner text is
/// element 14 in the window's control view and the button is 15.
/// </para>
/// <para>
/// This checks the CONDITION that produced it — a dismiss control declared before any text in its
/// own parent — so the next banner cannot reintroduce it by copying the old shape.
/// </para>
/// </summary>
public class DismissOrderFenceTests
{
    /// <summary>Element names that render words a user reads.</summary>
    private static readonly string[] TextLike = ["TextBlock", "Label", "AccessText"];

    [Fact]
    public void NoDismissControlIsDeclaredBeforeTheThingItDismisses()
    {
        var offenders = new List<string>();
        var dismissControls = 0;

        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            XDocument doc;
            try { doc = XDocument.Load(file.FullPath); }
            catch (System.Xml.XmlException) { continue; }

            foreach (var element in doc.Descendants())
            {
                if (!IsDismiss(element)) continue;
                dismissControls++;

                var parent = element.Parent;
                if (parent is null) continue;

                var siblings = parent.Elements().ToList();
                var index = siblings.IndexOf(element);

                // Only text declared BEFORE it counts. Text after it is text the user is offered the
                // dismiss button for without having heard yet.
                var textBefore = siblings.Take(index).Any(HasTextSomewhere);

                if (!textBefore)
                {
                    offenders.Add($"{file.Label}: {Describe(element)} is declared before any text in its parent <{parent.Name.LocalName}>");
                }
            }
        }

        // Vacuity floor: two banners today. A scan finding none would report a clean gate.
        Assert.True(dismissControls >= 2,
            $"Found only {dismissControls} dismiss controls. That is the scan breaking, not the app changing.");

        Assert.True(offenders.Count == 0,
            "A dismiss action announced before the thing it dismisses. Screen readers and Tab follow "
            + "DECLARATION order, so the user is offered \"Dismiss\" with nothing yet to dismiss. Use a "
            + "Grid with the message in a star column and the button in an Auto column — a DockPanel "
            + "cannot do this, because docking right requires declaring first:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>A control whose job is dismissing something, by accessible name or by content.</summary>
    private static bool IsDismiss(XElement element)
    {
        if (element.Name.LocalName is not ("Button" or "ToggleButton")) return false;

        var name = element.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == "Name" && a.Name.NamespaceName.Contains("automation", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        var content = element.Attribute("Content")?.Value;

        return (name?.StartsWith("Dismiss", StringComparison.OrdinalIgnoreCase) ?? false)
            || string.Equals(content, "Dismiss", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether this element, or anything inside it, carries words. Recursive because a banner's
    /// message is often a <c>TextBlock</c> wrapping <c>Run</c>s rather than a bare Text attribute —
    /// both live banners are exactly that shape.
    /// </summary>
    private static bool HasTextSomewhere(XElement element)
    {
        if (TextLike.Contains(element.Name.LocalName)) return true;
        if (element.Attribute("Text") is not null) return true;
        return element.Elements().Any(HasTextSomewhere);
    }

    private static string Describe(XElement element)
    {
        var name = element.Attributes()
            .FirstOrDefault(a => a.Name.LocalName == "Name")?.Value;
        return name is { Length: > 0 } ? $"<{element.Name.LocalName} Name=\"{name}\">" : $"<{element.Name.LocalName} Content=\"Dismiss\">";
    }
}
