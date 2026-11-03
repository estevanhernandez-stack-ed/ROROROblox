using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using ROROROblox.App.Localization;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Two shipped localization defects found in the v1.36 Store screenshot sweep (2026-11-03), each fenced here.
/// <list type="bullet">
/// <item>The shell's nav rail is a fixed 164 DIP column, and its labels did not wrap: "Bekannte Roblox-Proble" in German,
/// "Problemas conhecidos d" in Portuguese, in every Store screenshot of a shell page since localization.</item>
/// <item>Three About strings carried the XAML source's newline and indentation into every catalog, English included, so the
/// About paragraph broke mid-sentence with a deep indent. Same class as the tray tip fixed in item 10.</item>
/// </list>
/// </summary>
[Collection("MutatesUiCulture")]
public class ShellRailLabelTests
{
    private static readonly string[] Cultures = ["en", "de", "es", "fr", "pl", "pt-BR", "ru"];

    [WindowRenderFact]
    public void EveryRailLabel_FitsTheRail_InAtMostTwoLines_InEveryLanguage()
    {
        var theme = LibraryPageFixtures.Themes().Single(t => t.Id == "brand");
        var offenders = new List<string>();
        var seen = 0;
        foreach (var culture in Cultures)
        {
            var previous = TranslationSource.Instance.CurrentCulture;
            TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
            try
            {
                var rows = ThemedWindowRender.Inspect(theme, $"shell rail [{culture}]", () =>
                {
                    var shell = new App.Shell.ShellWindow(_ => new UserControl { Content = new TextBlock { Text = "page" } });
                    shell.NavigateTo(App.Shell.ShellPage.About);
                    return shell;
                }, root =>
                {
                    var nav = (ListBox)ThemedWindowRender.Find(root, fe => fe is ListBox { Name: "ShellNav" }, "ShellNav");
                    var result = new List<(string Text, double TextWidth, double Width, double Lines, TextWrapping Wrap)>();
                    foreach (var item in nav.Items.OfType<ListBoxItem>())
                    {
                        var tb = FindTextBlock(item) ?? throw new InvalidOperationException("a rail item has no TextBlock");
                        var ft = new FormattedText(tb.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                            new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.White,
                            VisualTreeHelper.GetDpi(tb).PixelsPerDip);
                        var lineHeight = tb.FontFamily.LineSpacing * tb.FontSize;
                        // Against the RAIL, not the TextBlock: an unwrapped TextBlock is arranged at its full text width and then
                        // clipped by its parent, so text-vs-TextBlock can never fail (this test's first version passed with the
                        // wrap removed). Where the text ends, in rail coordinates, against the rail's own width.
                        var left = tb.TransformToAncestor(nav).Transform(new Point(0, 0)).X;
                        var textRight = left + (tb.TextWrapping == TextWrapping.NoWrap ? ft.WidthIncludingTrailingWhitespace : tb.ActualWidth);
                        result.Add((tb.Text, textRight, nav.ActualWidth, tb.ActualHeight / lineHeight, tb.TextWrapping));
                    }

                    // The shell unsubscribes from CultureChanged in OnClosed. The harness never closes what it renders,
                    // so without this the next culture switch reaches this window from the test thread and throws.
                    Window.GetWindow(root)?.Close();
                    return result;
                });
                foreach (var r in rows)
                {
                    seen++;
                    if (r.TextWidth > r.Width + 0.5)
                        offenders.Add($"[{culture}] '{r.Text}' is cut: its text ends at {r.TextWidth:F1} DIP of a {r.Width:F1} DIP rail");
                    if (r.Lines > 2.3)
                        offenders.Add($"[{culture}] '{r.Text}' runs to {r.Lines:F1} lines");
                }
            }
            finally
            {
                TranslationSource.Instance.CurrentCulture = previous;
            }
        }

        Assert.True(seen >= Cultures.Length * 7, $"walked only {seen} rail labels; the selector stopped matching");
        Assert.True(offenders.Count == 0, "Shell rail labels:\n" + string.Join("\n", offenders));
    }

    private static TextBlock? FindTextBlock(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is TextBlock tb && !string.IsNullOrEmpty(tb.Text)) return tb;
            if (FindTextBlock(child) is { } found) return found;
        }

        return null;
    }

    [Fact]
    public void NoCatalogValue_CarriesXamlIndentation()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.GetFiles(dir, "ROROROblox.slnx").Any()) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var props = Path.Combine(dir!, "src", "ROROROblox.App", "Properties");
        var indented = new Regex(@"\n[ \t]{4,}");
        var offenders = new List<string>();
        var files = Directory.GetFiles(props, "Strings*.resx");
        Assert.True(files.Length >= 7, $"found {files.Length} catalogs");
        foreach (var file in files)
        {
            foreach (var data in XDocument.Load(file).Root!.Elements("data"))
            {
                var value = (string?)data.Element("value") ?? "";
                if (indented.IsMatch(value)) offenders.Add($"{Path.GetFileName(file)}: {(string?)data.Attribute("name")}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A catalog value carries a newline plus indentation (copied from XAML source); collapse it:\n" + string.Join("\n", offenders));
    }
}
