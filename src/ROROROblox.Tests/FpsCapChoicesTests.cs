using System.IO;
using System.Xml.Linq;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// The per-account FPS cap choices (2026-11-10). A Store review asked for caps that match a 75 Hz
/// and a 180 Hz monitor; the list offered neither. The dropdown's items are written by hand in
/// MainWindow.xaml and <see cref="FpsPresets.Values"/> is the canonical list, and nothing held the
/// two together.
/// </summary>
public class FpsCapChoicesTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(180)]
    public void ThePresets_IncludeTheCommonRefreshRatesTheListWasMissing(int hz)
    {
        Assert.Contains(hz, FpsPresets.Values);
    }

    [Fact]
    public void ThePresets_AreInAscendingOrder_WithNoRepeats()
    {
        var values = FpsPresets.Values.ToList();

        Assert.Equal(values.Distinct().OrderBy(v => v).ToList(), values);
    }

    [Fact]
    public void EveryPreset_IsInsideTheRangeTheStoreAccepts()
    {
        // AccountStore clamps a cap into this range, so a preset outside it would save as a
        // different number than the one the person picked.
        Assert.All(FpsPresets.Values, v => Assert.Equal(v, FpsPresets.ClampCustom(v)));
    }

    [Fact]
    public void TheDropdown_OffersNoCap_ThenExactlyThePresets_InOrder()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.NotNull(appDir);
        var doc = XDocument.Load(Path.Combine(appDir!, "MainWindow.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var combo = doc.Descendants(ns + "ComboBox")
            .Single(c => (string?)c.Attribute("SelectionChanged") == "FpsCombo_SelectionChanged");
        var tags = combo.Elements(ns + "ComboBoxItem").Select(i => (string?)i.Attribute("Tag")).ToList();

        Assert.Equal("{x:Null}", tags.First());
        Assert.Equal(
            FpsPresets.Values.Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList(),
            tags.Skip(1).ToList());
    }

    [Fact]
    public void EveryDropdownItem_HasItsOwnLabel_AndTheLabelNamesItsNumber()
    {
        // Each item's label is a string key such as MainWindow_144Fps. A copy-pasted item that
        // kept its neighbour's key would show the wrong number beside the right Tag.
        var appDir = XamlStyleScanner.AppSourceDirectory();
        var doc = XDocument.Load(Path.Combine(appDir!, "MainWindow.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var combo = doc.Descendants(ns + "ComboBox")
            .Single(c => (string?)c.Attribute("SelectionChanged") == "FpsCombo_SelectionChanged");

        foreach (var item in combo.Elements(ns + "ComboBoxItem"))
        {
            var tag = (string?)item.Attribute("Tag");
            var content = (string?)item.Attribute("Content") ?? string.Empty;
            if (tag is "{x:Null}" or "9999") continue; // "no cap" and Unlimited are named in words

            Assert.Equal($"{{loc:Loc MainWindow_{tag}Fps}}", content);
            Assert.Equal($"{tag} FPS", App.Localization.Loc.Get($"MainWindow_{tag}Fps"));
        }
    }
}
