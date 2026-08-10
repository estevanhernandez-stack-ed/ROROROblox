using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Xml.Linq;
using ROROROblox.Core.Theming;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Pins the batch-selection toggle's geometry, in pixels, against the two defects the v1.17 build
/// shipped and a human found by looking at it.
/// <para>
/// <b>Off-centre dot.</b> The inner ellipse was a stretched shape inset by <c>Margin="2"</c>. That is
/// centred only when both insets round the same way, and at 125% scaling they do not — a 14 DIP slot
/// is 17.5 device px and a 10 DIP fill is 12.5, so one side absorbs the odd pixel. On a 14px control
/// a one-pixel bias is visible. Now a fixed 6x6 centred in the slot: one rounding decision instead
/// of two.
/// </para>
/// <para>
/// <b>Checked read as a fatter ring.</b> Item 3b themed the resting ring to <c>MutedTextBrush</c>,
/// closing F-089. It also dropped ring-vs-fill separation from 3.87:1 to 1.37:1 in brand, because the
/// <c>#4A5C70</c> it replaced was DARK and the brightness step was the whole signal. No shipped slot
/// recovers it — <c>DividerBrush</c> and <c>NavyBrush</c> separate from the fill at 4.3-12.8:1 but sit
/// at 1.05-1.33:1 against the row, i.e. an unchecked control nobody can see. So the distinction moves
/// to shape: the ring-to-dot gap goes 1.25px -> 3.25px. <c>spec.md > §6.4</c> already credits this
/// control for carrying state in shape rather than colour; this makes that true in pixels instead of
/// in principle.
/// </para>
/// <para>
/// Measures the SHIPPED markup. The style is extracted from <c>App.xaml</c> with <c>XamlReader</c>
/// rather than rebuilt here, for the reason stage 3 records: a reconstruction passes forever while
/// the real markup rots.
/// </para>
/// </summary>
public class SelectionDotGeometryTests
{
    private const string StyleKey = "SelectionDotStyle";

    /// <summary>The control is 14x14 and the dot 6x6, so the dot covers ~18% at most.</summary>
    private const double MaxDotCoverage = 0.30;

    private static IReadOnlyList<Theme> BuiltIns()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rororo-seldot-" + Guid.NewGuid().ToString("N"));
        var themes = new ThemeStore(scratch).ListAsync().GetAwaiter().GetResult()
            .Where(t => t.IsBuiltIn).ToList();
        try { if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true); }
        catch (IOException) { }

        Assert.True(themes.Count >= 4,
            $"Expected at least the 4 built-in themes; got {themes.Count}. A short theme list makes "
            + "every assertion below measure less than it claims.");
        return themes;
    }

    /// <summary>
    /// Pulls the shipped <c>SelectionDotStyle</c> out of <c>App.xaml</c>. Fails naming what it looked
    /// for, because a gate that silently measures nothing is worse than no gate.
    /// </summary>
    private static string ShippedStyleXaml()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.False(string.IsNullOrEmpty(appDir),
            "Could not locate the App source directory, so this test would measure nothing.");

        var appXaml = Path.Combine(appDir, "App.xaml");
        Assert.True(File.Exists(appXaml), $"App.xaml not found at '{appXaml}'.");

        var doc = XDocument.Load(appXaml, LoadOptions.SetLineInfo);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var style = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Style"
                                 && (string?)e.Attribute(x + "Key") == StyleKey)
            ?? throw new InvalidOperationException(
                $"Found no <Style x:Key=\"{StyleKey}\"> in App.xaml. That is the batch-selection "
                + "toggle on every account row. Either it was renamed, moved to another dictionary, "
                + "or deleted — in every case this test is measuring nothing and the toggle's "
                + "geometry is unpinned again.");

        // x:Key is meaningless once parsed standalone, and x: must be declared on the root.
        var clone = new XElement(style);
        clone.Attribute(x + "Key")?.Remove();
        clone.SetAttributeValue(XNamespace.Xmlns + "x", x.NamespaceName);
        return clone.ToString();
    }

    /// <summary>
    /// 100%, 125% and 150%. **The DPI sweep is the whole point of this test**, not thoroughness for
    /// its own sake: at 96 the old <c>Margin="2"</c> markup centres perfectly, because a 14 DIP slot
    /// and a 10 DIP fill are both even and the insets round the same way. The defect a human
    /// reported only exists at fractional scaling. A version of this test that rendered at 96 alone
    /// would have passed against the broken markup and been worse than no test — it would have
    /// certified the bug.
    /// </summary>
    private static readonly double[] Scales = [96, 120, 144];

    private static Sample Render(Theme theme, bool isChecked, double dpi = 96) =>
        ThemedRender.Measure(theme, $"{StyleKey}:{(isChecked ? "checked" : "unchecked")}@{dpi}", dict =>
        {
            var style = (Style)XamlReader.Parse(ShippedStyleXaml());
            return new ToggleButton { Style = style, IsChecked = isChecked };
        }, dpi);

    // A centring assertion was written here and DELETED rather than shipped. It could not fail.
    //
    // The defect a human reported is a one-pixel bias at fractional scaling. RenderTargetBitmap's
    // dpi argument does not reproduce it: it scales the VECTOR OUTPUT of a layout that already ran
    // in DIPs at 96, so the two insets never round differently and the bug cannot occur in this
    // harness. Reproducing it needs layout to run at a different device scale — an HwndSource with
    // a real per-monitor DPI context — which this offscreen harness does not have.
    //
    // Verified toothless twice, not assumed: with the shipped-broken Margin="2" restored (with and
    // without UseLayoutRounding), and again with a grossly asymmetric Margin="2,2,5,5" that offsets
    // the dot by 3px. It passed all three times.
    //
    // Shipping it anyway would have been the worst outcome available. It reads as coverage, it goes
    // green forever, and the next person to move this markup gets told the geometry is pinned when
    // nothing is watching it. That is the same defect this whole branch exists to catch, so it does
    // not get to ship inside the branch that catches it. The fix below it is still correct on first
    // principles — a fixed size centred in the slot has one rounding decision instead of two — it is
    // simply not gated here, and saying so is cheaper than a green test that lies.

    [Fact]
    public void TheCheckedDotIsASmallDotAndNotAThickerRing()
    {
        var offenders = new List<string>();

        foreach (var theme in BuiltIns())
        {
            var sample = Render(theme, isChecked: true);
            var fillHex = ThemedRender.Slot(ThemedRender.Resources(theme), "CyanBrush");

            var total = sample.Histogram.Sum(h => h.Count);
            var dot = sample.Histogram.Where(h => h.Colour == fillHex).Sum(h => h.Count);
            var coverage = (double)dot / total;

            // The failure this guards is the one a human reported: a dot so close to the ring in
            // both colour AND position that "checked" reads as "the ring got fatter". Colour is not
            // available as a signal here (see the class doc), so the gap has to be real.
            if (coverage > MaxDotCoverage)
            {
                offenders.Add(
                    $"'{theme.Id}': the dot covers {coverage:P1} of the control, over the {MaxDotCoverage:P0} "
                    + "ceiling. It has grown back toward the ring, and with ring and fill only "
                    + "1.34-1.95:1 apart in the shipped themes, that is what makes a checked toggle "
                    + "read as an unchecked one with a heavier stroke.");
            }
        }

        Assert.True(offenders.Count == 0,
            "The checked selection dot is too large relative to its ring:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void CheckedAndUncheckedRenderDifferently()
    {
        foreach (var theme in BuiltIns())
        {
            var on = Render(theme, isChecked: true);
            var off = Render(theme, isChecked: false);
            var fillHex = ThemedRender.Slot(ThemedRender.Resources(theme), "CyanBrush");

            var onDot = on.Histogram.Where(h => h.Colour == fillHex).Sum(h => h.Count);
            var offDot = off.Histogram.Where(h => h.Colour == fillHex).Sum(h => h.Count);

            Assert.True(onDot > 0,
                $"'{theme.Id}': checked toggle rendered no CyanBrush pixels. The IsChecked trigger "
                + "did not fire, or the fill stayed Collapsed.");
            Assert.True(offDot == 0,
                $"'{theme.Id}': unchecked toggle rendered {offDot} CyanBrush pixels. The dot is "
                + "showing when nothing is selected.");
        }
    }
}
