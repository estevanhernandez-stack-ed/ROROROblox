using System.IO;
using System.Reflection;
using System.Windows;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// Static validation of every <c>&lt;Style&gt;</c> in every XAML file in the app.
/// <para>
/// WHY THIS EXISTS. On 2026-05-04 <c>GamesWindow.xaml</c> shipped
/// <c>&lt;Style x:Key="InverseBoolToVisibilityStyle" TargetType="UIElement"&gt;</c>. UIElement does
/// not derive from FrameworkElement, so <c>Style.TargetType</c> rejects it and constructing the
/// Style throws. It survived three months and roughly two dozen releases behind a fully green test
/// suite, because nothing in this repo loads XAML — every test targets view-models and services.
/// </para>
/// <para>
/// WHY NOT A SMOKE TEST THAT CONSTRUCTS EACH WINDOW. It would not have caught that bug. WPF
/// ResourceDictionaries create their resources LAZILY: the Style above is only constructed when
/// <c>{StaticResource InverseBoolToVisibilityStyle}</c> is first resolved, which happens when a
/// row's DataTemplate is applied. Construct GamesWindow with an empty game library and it loads
/// perfectly. Catching it that way needs every window AND populated data for every branch that
/// renders a different template. This scanner reads the markup instead, so data state is
/// irrelevant and coverage is every file, not every path someone remembered to exercise.
/// </para>
/// <para>
/// WHAT THIS DOES NOT COVER, so nobody mistakes it for more than it is: bindings to properties
/// that do not exist (WPF fails those silently, so no static or runtime check sees them without
/// watching the trace log), and <c>{StaticResource}</c> keys that resolve nowhere (the key universe
/// includes WPF-UI's compiled dictionaries, which are not readable from markup). Those remain
/// uncovered.
/// </para>
/// </summary>
public class XamlStyleIntegrityTests
{
    [Fact]
    public void EveryStyleInEveryXamlFile_IsConstructible()
    {
        var files = XamlStyleScanner.EnumerateAppXamlFiles().ToList();

        // A path bug that finds zero files would make this test vacuously green — the exact failure
        // mode it exists to prevent. The app has ~33 XAML files; anything near zero is a broken scan.
        Assert.True(files.Count >= 20, $"Expected to scan the app's XAML files, found {files.Count}. The repo-root walk probably failed.");

        var problems = files
            .SelectMany(f => XamlStyleScanner.Scan(File.ReadAllText(f.FullPath), f.Label))
            .ToList();

        Assert.True(problems.Count == 0, "Styles that will throw when WPF constructs them:" + Environment.NewLine + string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Scanner_FlagsATargetTypeThatIsNotAFrameworkElement()
    {
        // Verbatim shape of the GamesWindow bug. If this test ever goes green-by-vacuum — scanner
        // silently finding nothing — the suite above stops meaning anything, so pin it here.
        const string xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Window.Resources>
                <Style x:Key="InverseBoolToVisibilityStyle" TargetType="UIElement" />
              </Window.Resources>
            </Window>
            """;

        var problems = XamlStyleScanner.Scan(xaml, "sample.xaml");

        Assert.Single(problems);
        Assert.Contains("UIElement", problems[0]);
    }

    [Fact]
    public void Scanner_FlagsASetterPropertyThatDoesNotExistOnTheTargetType()
    {
        const string xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Window.Resources>
                <Style TargetType="Button">
                  <Setter Property="Foregorund" Value="Red" />
                </Style>
              </Window.Resources>
            </Window>
            """;

        var problems = XamlStyleScanner.Scan(xaml, "sample.xaml");

        Assert.Single(problems);
        Assert.Contains("Foregorund", problems[0]);
    }

    [Fact]
    public void Scanner_IgnoresSettersThatTargetANamedTemplateChild()
    {
        // A TargetName setter addresses an element inside the ControlTemplate, not the styled type.
        // Visibility is not a Rectangle-only property, so use one that genuinely is not on Button:
        // without the TargetName skip this would be a false positive, and false positives are what
        // get a test like this deleted six months from now.
        const string xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Window.Resources>
                <Style TargetType="Button">
                  <Style.Triggers>
                    <Trigger Property="IsPressed" Value="True">
                      <Setter TargetName="Marker" Property="RadiusX" Value="3" />
                    </Trigger>
                  </Style.Triggers>
                </Style>
              </Window.Resources>
            </Window>
            """;

        Assert.Empty(XamlStyleScanner.Scan(xaml, "sample.xaml"));
    }

    [Fact]
    public void Scanner_AcceptsTheShapesTheAppActuallyUses()
    {
        // Guards against the other failure direction: a scanner so strict that real markup trips it.
        const string xaml = """
            <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
              <Window.Resources>
                <Style TargetType="{x:Type Button}" BasedOn="{StaticResource {x:Type Button}}">
                  <Setter Property="Foreground" Value="Red" />
                  <Setter Property="Grid.Row" Value="1" />
                </Style>
                <Style TargetType="ui:CardControl" />
                <Style TargetType="TextBlock">
                  <Style.Triggers>
                    <DataTrigger Binding="{Binding IsCompact}" Value="True">
                      <Setter Property="Visibility" Value="Collapsed" />
                    </DataTrigger>
                  </Style.Triggers>
                </Style>
              </Window.Resources>
            </Window>
            """;

        Assert.Empty(XamlStyleScanner.Scan(xaml, "sample.xaml"));
    }
}

/// <summary>
/// Reads XAML as markup and reports Styles WPF would refuse to construct. Deliberately reports
/// nothing when a type or property cannot be resolved: a scanner that guesses produces false
/// positives, and a test with false positives gets deleted rather than fixed.
/// </summary>
internal static class XamlStyleScanner
{
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    internal readonly record struct XamlFile(string FullPath, string Label);

    /// <summary>The app project's source directory, or null when the repo-root walk fails.</summary>
    internal static string? AppSourceDirectory()
    {
        var root = FindRepoRoot();
        if (root is null) return null;

        var appDir = Path.Combine(root, "src", "ROROROblox.App");
        return Directory.Exists(appDir) ? appDir : null;
    }

    /// <summary>Every .xaml under the app project, located by walking up to the solution file.</summary>
    internal static IEnumerable<XamlFile> EnumerateAppXamlFiles()
    {
        var root = FindRepoRoot();
        var appDir = AppSourceDirectory();
        if (root is null || appDir is null) yield break;

        foreach (var path in Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories))
        {
            // obj/ holds generated copies; scanning them double-reports every finding.
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            yield return new XamlFile(path, Path.GetRelativePath(root, path).Replace('\\', '/'));
        }
    }

    /// <summary>Walks up from the test assembly until it finds the tracked solution file.</summary>
    internal static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ROROROblox.slnx"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    internal static IReadOnlyList<string> Scan(string xamlText, string label)
    {
        var problems = new List<string>();

        XDocument doc;
        try { doc = XDocument.Parse(xamlText, LoadOptions.SetLineInfo); }
        catch (System.Xml.XmlException ex)
        {
            // Malformed XAML is itself a build failure, but report rather than throw so one bad
            // file does not hide findings in the other thirty-two.
            problems.Add($"{label}: not well-formed XML — {ex.Message}");
            return problems;
        }

        foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
        {
            var rawTargetType = style.Attribute("TargetType")?.Value;
            if (string.IsNullOrWhiteSpace(rawTargetType)) continue;   // BasedOn-only or template-local

            var targetType = ResolveType(rawTargetType, style);
            if (targetType is null) continue;                          // unresolvable → stay quiet

            if (!typeof(FrameworkElement).IsAssignableFrom(targetType)
                && !typeof(FrameworkContentElement).IsAssignableFrom(targetType))
            {
                problems.Add(
                    $"{label}{Line(style)}: Style TargetType=\"{rawTargetType}\" resolves to {targetType.FullName}, " +
                    "which derives from neither FrameworkElement nor FrameworkContentElement. WPF throws " +
                    "XamlParseException when it constructs this Style — and because ResourceDictionaries are lazy, " +
                    "that happens the first time something resolves the key, not at window load.");
                continue;
            }

            foreach (var setter in style.Descendants().Where(e => e.Name.LocalName == "Setter"))
            {
                // Only setters belonging to THIS style — a nested Style (inside a Setter's Value)
                // owns its own, against its own TargetType.
                if (NearestStyle(setter) != style) continue;

                // TargetName addresses a named child inside the ControlTemplate, whose type is not
                // TargetType. Out of scope; checking it would need template resolution.
                if (setter.Attribute("TargetName") is not null) continue;

                var property = setter.Attribute("Property")?.Value;
                if (string.IsNullOrWhiteSpace(property)) continue;

                if (!DependencyPropertyExists(property, targetType, setter))
                {
                    problems.Add(
                        $"{label}{Line(setter)}: Setter Property=\"{property}\" is not a dependency property " +
                        $"on {targetType.Name} (Style TargetType=\"{rawTargetType}\"). WPF throws when it applies this Setter.");
                }
            }
        }

        return problems;
    }

    private static XElement? NearestStyle(XElement element)
    {
        for (var p = element.Parent; p is not null; p = p.Parent)
        {
            if (p.Name.LocalName == "Style") return p;
        }
        return null;
    }

    private static string Line(XElement e) =>
        e is System.Xml.IXmlLineInfo li && li.HasLineInfo() ? $":{li.LineNumber}" : "";

    /// <summary>
    /// Resolves a XAML type reference — <c>Button</c>, <c>{x:Type Button}</c>, <c>ui:CardControl</c> —
    /// against the assemblies WPF would search. Returns null rather than guessing.
    /// </summary>
    private static Type? ResolveType(string raw, XElement context)
    {
        var name = raw.Trim();

        if (name.StartsWith("{", StringComparison.Ordinal))
        {
            // {x:Type Button} / {x:Type ui:CardControl}
            var inner = name.Trim('{', '}').Trim();
            const string marker = "x:Type";
            var at = inner.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return null;
            name = inner[(at + marker.Length)..].Trim();
        }

        var colon = name.IndexOf(':');
        if (colon >= 0) name = name[(colon + 1)..];   // drop the xmlns prefix; match on simple name

        if (name.Length == 0) return null;

        var matches = SearchAssemblies.Value
            .SelectMany(SafeTypes)
            .Where(t => t.Name == name)
            .Distinct()
            .ToList();

        if (matches.Count == 1) return matches[0];
        if (matches.Count == 0) return null;

        // Ambiguous simple name (Button exists in both System.Windows.Controls and Wpf.Ui.Controls).
        // A bare <Style TargetType="Button"> in the default XAML namespace means the WPF one, and
        // WPF-UI restyles that type rather than replacing it. Prefer the framework type; if the
        // markup meant the other, both derive from FrameworkElement anyway, so the verdict matches.
        return matches.FirstOrDefault(t => t.Namespace?.StartsWith("System.Windows", StringComparison.Ordinal) == true)
               ?? matches[0];
    }

    private static IEnumerable<Type> SafeTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    private static readonly Lazy<Assembly[]> SearchAssemblies = new(() =>
    {
        var list = new List<Assembly>
        {
            typeof(FrameworkElement).Assembly,        // PresentationFramework
            typeof(UIElement).Assembly,               // PresentationCore
            typeof(DependencyObject).Assembly,        // WindowsBase
            typeof(ROROROblox.App.ViewModels.MainViewModel).Assembly,
        };

        // WPF-UI ships the ui: types. Loaded by name because the test project references the app,
        // not the package directly.
        try { list.Add(Assembly.Load("Wpf.Ui")); } catch (Exception) { /* ui: types stay unresolvable, which means silent */ }

        return list.Distinct().ToArray();
    });

    /// <summary>
    /// True when <paramref name="property"/> names a dependency property reachable from
    /// <paramref name="targetType"/> — either its own, or an attached one like <c>Grid.Row</c>.
    /// Unresolvable owners return true (stay quiet) rather than false (cry wolf).
    /// </summary>
    private static bool DependencyPropertyExists(string property, Type targetType, XElement context)
    {
        var dot = property.LastIndexOf('.');
        if (dot >= 0)
        {
            var ownerName = property[..dot];
            var propName = property[(dot + 1)..];
            var ownerType = ResolveType(ownerName, context);
            return ownerType is null || HasDependencyPropertyField(ownerType, propName);
        }

        return HasDependencyPropertyField(targetType, property);
    }

    private static bool HasDependencyPropertyField(Type type, string propertyName)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var field = t.GetField(propertyName + "Property", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (field is not null && typeof(DependencyProperty).IsAssignableFrom(field.FieldType)) return true;

            // A few WPF properties are exposed as DependencyPropertyKey-backed read-only pairs, and
            // some are plain CLR properties usable in a Setter only when they are DPs — but the
            // public *Property field is the reliable signal, so also accept an ordinary property of
            // the same name to avoid flagging edge cases we do not fully model.
            if (t.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy) is not null) return true;
        }
        return false;
    }
}
