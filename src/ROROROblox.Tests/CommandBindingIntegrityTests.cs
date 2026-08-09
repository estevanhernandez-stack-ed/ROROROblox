using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Input;
using ROROROblox.App.ViewModels;

namespace ROROROblox.Tests;

/// <summary>
/// Closes a gap <see cref="XamlStyleIntegrityTests"/> names but does not cover: "bindings to
/// properties that do not exist (WPF fails those silently, so no static or runtime check sees them
/// without watching the trace log)".
/// <para>
/// F-001 added three Command bindings to the Tools menu. A typo in any of them produces a menu item
/// that renders perfectly and does nothing at all, behind a fully green suite. This reads the markup
/// and resolves each binding against the view-model.
/// </para>
/// <para>
/// Scoped to MainWindow -> MainViewModel deliberately. Widening it to every window needs a
/// window-to-view-model map that does not exist yet; a narrow gate that runs beats a broad one that
/// does not ship.
/// </para>
/// </summary>
public class CommandBindingIntegrityTests
{
    [Fact]
    public void EveryMainWindowCommandBindingResolvesToAnICommand()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.NotNull(appDir);

        var xamlPath = Path.Combine(appDir!, "MainWindow.xaml");
        Assert.True(File.Exists(xamlPath), $"Expected MainWindow.xaml at {xamlPath}");

        var markup = File.ReadAllText(xamlPath);

        // Command="{Binding Foo}" and Command="{Binding Path=Foo}". Bindings that walk a path
        // (Foo.Bar) or target another source are out of scope for this gate.
        var matches = Regex.Matches(
            markup,
            @"Command\s*=\s*""\{Binding\s+(?:Path\s*=\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\}""");

        Assert.True(matches.Count > 0, "Found no Command bindings in MainWindow.xaml — the regex is wrong.");

        var vmType = typeof(MainViewModel);
        var missing = new List<string>();

        foreach (Match m in matches)
        {
            var name = m.Groups["name"].Value;
            var prop = vmType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

            if (prop is null)
            {
                missing.Add($"{name} (no such public property on MainViewModel)");
            }
            else if (!typeof(ICommand).IsAssignableFrom(prop.PropertyType))
            {
                missing.Add($"{name} (is {prop.PropertyType.Name}, not ICommand)");
            }
        }

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("StopAllCommand")]
    [InlineData("OpenLogFolderCommand")]
    [InlineData("ShowWelcomeTourCommand")]
    public void TheThreeF001VerbsAreBoundInTheToolsMenu(string commandName)
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.NotNull(appDir);

        var markup = File.ReadAllText(Path.Combine(appDir!, "MainWindow.xaml"));

        Assert.Contains($"{{Binding {commandName}}}", markup, StringComparison.Ordinal);
    }
}
