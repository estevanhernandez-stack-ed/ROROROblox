using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ROROROblox.App.Localization;
using ROROROblox.App.Theming;
using ROROROblox.Core.Theming;
using Xunit;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// Task kw9psaaah (v1.41 item 14 rider): the Build a theme dialog's button row ran past the window's left edge in
/// the longer languages, so the first button, "Open themes folder", was cut off. Every button in the row must lie
/// inside the window's content, at the default width (640) and at the minimum (540), in all seven languages.
/// </summary>
[Collection("MutatesUiCulture")]
public class ThemeBuilderButtonRowFitTests
{
    private static readonly string[] Cultures = ["en", "de", "es", "fr", "pl", "pt-BR", "ru"];

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ThemeBuilderButtonRowFitTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private sealed record ButtonFit(string Name, double Left, double Right, double ContentWidth);

    private static Theme Brand() => LibraryPageFixtures.Themes().Single(t => t.Id == "brand");

    private static IReadOnlyList<ButtonFit> Measure(string culture, double width)
    {
        var previous = TranslationSource.Instance.CurrentCulture;
        TranslationSource.Instance.CurrentCulture = new CultureInfo(culture);
        var themes = Path.Combine(Path.GetTempPath(), "rororo-theme-fit-" + Guid.NewGuid().ToString("N"));
        try
        {
            return ThemedWindowRender.Inspect(
                Brand(),
                $"theme builder row [{culture} @{width}]",
                () =>
                {
                    var service = new ThemeService(new ThemeStore(themes), new MainViewModelTests.FakeAppSettings());
                    return new ThemeBuilderWindow(new ThemeStore(themes), service) { Width = width };
                },
                content =>
                {
                    var row = ThemedWindowRender.Find(content, fe => fe.Name == "ButtonRow", "ButtonRow");
                    // The bound is the declared width less the root's margins, not content.ActualWidth: once nothing
                    // overflows, the harness arranges the root at its desired width, which a right-edge check
                    // measured against itself could never fail.
                    var limit = width - content.Margin.Left - content.Margin.Right;
                    var fits = new List<ButtonFit>();
                    foreach (var button in Descendants(row).OfType<Button>().Where(b => b.Visibility == Visibility.Visible))
                    {
                        var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
                        fits.Add(new ButtonFit(button.Content?.ToString() ?? button.Name, bounds.Left, bounds.Right, limit));
                    }
                    return (IReadOnlyList<ButtonFit>)fits;
                });
        }
        finally
        {
            TranslationSource.Instance.CurrentCulture = previous;
            try { Directory.Delete(themes, recursive: true); } catch (IOException) { }
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            if (child is not Button)
            {
                foreach (var d in Descendants(child)) yield return d;
            }
        }
    }

    [WindowRenderFact]
    public void EveryButtonInTheRow_IsInsideTheWindow_AtTheDefaultAndMinimumWidths_InEveryLanguage()
    {
        var outside = new List<string>();
        var measured = 0;
        foreach (var width in new[] { 640.0, 540.0 })
        {
            foreach (var culture in Cultures)
            {
                var fits = Measure(culture, width);
                Assert.Equal(5, fits.Count);
                foreach (var f in fits)
                {
                    measured++;
                    _output.WriteLine($"[{culture} @{width}] '{f.Name}' {f.Left:F1}..{f.Right:F1} of {f.ContentWidth:F1}");
                    if (f.Left < -0.5 || f.Right > f.ContentWidth + 0.5)
                    {
                        outside.Add($"[{culture} @{width}] '{f.Name}' at {f.Left:F1}..{f.Right:F1}, content {f.ContentWidth:F1}");
                    }
                }
            }
        }

        Assert.Equal(2 * Cultures.Length * 5, measured);
        Assert.True(outside.Count == 0, "Build a theme buttons outside the window: " + string.Join("; ", outside));
    }
}
