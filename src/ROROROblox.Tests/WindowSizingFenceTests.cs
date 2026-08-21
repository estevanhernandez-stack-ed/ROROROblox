using System.IO;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// F-115 — a window that cannot be resized must not also guess its own height.
/// <para>
/// WHAT THIS IS FOR. <c>AboutWindow</c> shipped its trademark disclaimer cut mid-sentence, on a
/// Store-listed product, for as long as its current copy had existed. Nothing caught it: no
/// exception, no log line, and no render gate, because they all measure pixels INSIDE the frame and
/// never content past its bottom edge (F-113). The trigger was a fixed <c>Height</c> on a
/// <c>NoResize</c> window, and the reason it was silent was a <c>*</c> grid row — a star row
/// absorbs a shortfall by handing its child less than it asked for, so the layout SUCCEEDS and the
/// text simply is not there.
/// </para>
/// <para>
/// A height is a guess about font metrics, DPI, and how one particular sentence happens to wrap.
/// The next line of copy re-breaks it, and the window has no way to say so. <c>SizeToContent</c>
/// asks the content instead, which is what two windows in this app were already doing before F-115
/// converted the other twelve.
/// </para>
/// <para>
/// WHY THE RULE STOPS AT HEIGHT. The star-row half is the real mechanism but it is not safely
/// expressible: <c>ConsentSheet</c> keeps a scroller whose length is the plugin's manifest, and it
/// is correct precisely because its cap sits on the Border rather than the row. A gate that banned
/// star rows would ban the one window that thought about it. So this checks the condition that
/// makes clipping possible and unreportable — an unresizable frame with a number in it — and the
/// star-row hazard is documented here rather than enforced.
/// </para>
/// </summary>
public class WindowSizingFenceTests
{
    [Fact]
    public void NoUnresizableWindowDeclaresAFixedHeightWithoutSizingToItsContent()
    {
        var offenders = new List<string>();
        var checkedWindows = 0;

        foreach (var (label, root) in UnresizableWindows())
        {
            checkedWindows++;

            var height = root.Attribute("Height")?.Value;
            if (string.IsNullOrWhiteSpace(height)) continue;
            if (!double.TryParse(height, out _)) continue;

            if (root.Attribute("SizeToContent") is null)
            {
                offenders.Add($"{label}: ResizeMode=\"NoResize\" with Height=\"{height}\" and no SizeToContent");
            }
        }

        // Vacuity floor. This walk climbs the filesystem from the test assembly, so a moved output
        // directory would find nothing and report a clean gate. 10 sits under the 14 measured on
        // 2026-08-21 and over anything a real consolidation would reach.
        Assert.True(checkedWindows >= 10,
            $"Found only {checkedWindows} NoResize windows. That is the scan breaking, not the app changing.");

        Assert.True(offenders.Count == 0,
            "A window that cannot be resized and carries a fixed height cannot report that its "
            + "content no longer fits — it just stops drawing the bottom of it (F-113, F-115). Use "
            + "SizeToContent=\"Height\" with Auto rows; when the content is genuinely unbounded, cap "
            + "the scrolling element rather than the window, as ConsentSheet does:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheWindowsThatSizeToContentDoNotAlsoPinAHeight()
    {
        // Both together is a contradiction WPF resolves by ignoring one of them, and which one is
        // not obvious from reading the markup. If a window needs a bound, MaxHeight says so out loud.
        var offenders = new List<string>();

        foreach (var (label, root) in UnresizableWindows())
        {
            if (root.Attribute("SizeToContent") is null) continue;
            if (root.Attribute("Height") is { } h)
            {
                offenders.Add($"{label}: SizeToContent and Height=\"{h.Value}\" — use MaxHeight if a cap is wanted");
            }
        }

        Assert.True(offenders.Count == 0,
            "SizeToContent and a fixed Height contradict each other:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Root elements of app XAML files whose window is explicitly not resizable.</summary>
    private static IEnumerable<(string Label, XElement Root)> UnresizableWindows()
    {
        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            XDocument doc;
            try { doc = XDocument.Load(file.FullPath); }
            catch (System.Xml.XmlException) { continue; }

            var root = doc.Root;
            if (root is null) continue;
            if (!root.Name.LocalName.EndsWith("Window", StringComparison.Ordinal)) continue;
            if (root.Attribute("ResizeMode")?.Value != "NoResize") continue;

            yield return (Path.GetFileName(file.FullPath), root);
        }
    }
}
