using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// Keeps the prose token off control labels (F-032).
/// <para>
/// <c>MutedTextBrush</c> does two jobs. On helper text, empty states and chips it is correct and
/// there are ~104 of those. On a control's label it is the defect: muted-vs-white measures 2.42:1
/// in the brand theme and 1.00:1 under flatline, so under a theme the app supports there is nothing
/// separating a control's label from the paragraph beside it. F-031 already shipped the affordance
/// that does the separating — <c>InteractiveEdgeBrush</c>, derived to clear 3:1 under any theme.
/// </para>
/// <para>
/// This is a role fence, not a token ban. Prose keeps the token. What it may not do is label a
/// control.
/// </para>
/// </summary>
public class MutedTextFenceTests
{
    private const string ProseToken = "{DynamicResource MutedTextBrush}";

    /// <summary>
    /// Element types that ARE controls by type. Anything outside this list can still be a control
    /// by behaviour — see <see cref="IsControl"/>.
    /// </summary>
    private static readonly string[] ControlElements =
    [
        "Button", "MenuItem", "ToggleButton", "CheckBox", "RadioButton",
        "ComboBox", "ComboBoxItem", "ListBoxItem", "TabItem", "Hyperlink",
        "TextBox", "PasswordBox", "Slider", "ToggleSwitch", "RepeatButton",
    ];

    /// <summary>
    /// Control by type OR by behaviour. <c>LocalName</c> ignores the xmlns prefix, so
    /// <c>ui:ToggleSwitch</c> matches "ToggleSwitch".
    /// </summary>
    private static bool IsControl(XElement el) =>
        ControlElements.Contains(el.Name.LocalName) || XamlStyleScanner.IsInteractive(el);

    private static IEnumerable<(XDocument Doc, string Label)> AppXaml()
    {
        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            XDocument doc;
            try { doc = XDocument.Load(file.FullPath, LoadOptions.SetLineInfo); }
            catch (System.Xml.XmlException) { continue; }
            yield return (doc, file.Label);
        }
    }

    private static int LineOf(XObject o) =>
        o is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;

    /// <summary>
    /// The type of the object being constructed above <paramref name="index"/>, or null. Used only
    /// by the code-behind clause: an object initialiser sets Foreground several lines below its
    /// `new Type`, so the line itself does not say what it is decorating.
    /// </summary>
    private static string? NearestConstructedType(string[] lines, int index)
    {
        for (var i = index; i >= 0 && i > index - 15; i--)
        {
            // Anchored to end-of-line on purpose. Every multi-line object initialiser in this
            // codebase is written Allman-style — `new Button` alone on its line, `{` on the next —
            // so that shape is the actual object the Foreground line belongs to. A single-line
            // value construction like `Padding = new Thickness(10, 6, 10, 6),` also matches
            // `new\s+([A-Z]...)` but has more text after the type name on the same line; without the
            // `\s*$` anchor this helper returns "Thickness" for a line several rows above the real
            // `new Button`, which is exactly why the code-behind clause below found zero offenders
            // instead of the one (SquadLaunchWindow.xaml.cs) it exists to catch.
            var m = System.Text.RegularExpressions.Regex.Match(lines[i], @"new\s+([A-Z][A-Za-z0-9_]*)\s*$");
            if (m.Success) return m.Groups[1].Value;
        }

        return null;
    }

    [Fact]
    public void NoControlLabelBindsTheProseToken()
    {
        var offenders = new List<string>();

        foreach (var (doc, label) in AppXaml())
        {
            foreach (var el in doc.Descendants())
            {
                if (!IsControl(el)) continue;

                var fg = el.Attribute("Foreground");
                if (fg is not null && fg.Value == ProseToken)
                {
                    offenders.Add($"{label}:{LineOf(el)} <{el.Name.LocalName}> Foreground={ProseToken}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A control's label may not use the prose token. Bind WhiteBrush and let weight plus "
            + "InteractiveEdgeBrush carry 'secondary':\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoControlStyleSetsTheProseTokenAsForeground()
    {
        // THE LOAD-BEARING CLAUSE. A <Style> is not an interactive element, so the element fence
        // above is structurally blind to it — which is exactly how this defect reached 18 buttons
        // at once when the shared dictionary was written. The failure mode was centralisation, so
        // this watches the centre.
        var offenders = new List<string>();

        foreach (var (doc, label) in AppXaml())
        {
            foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
            {
                var target = style.Attribute("TargetType")?.Value ?? "";
                var targetName = target.Split('.', ':').Last().Trim('}', ' ');
                if (!ControlElements.Contains(targetName)) continue;

                foreach (var setter in style.Descendants().Where(e => e.Name.LocalName == "Setter"))
                {
                    if (setter.Attribute("Property")?.Value == "Foreground"
                        && setter.Attribute("Value")?.Value == ProseToken)
                    {
                        offenders.Add(
                            $"{label}:{LineOf(setter)} Style TargetType={targetName} sets Foreground={ProseToken}");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A control style may not set the prose token as its foreground — one setter reaches "
            + "every call site at once:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoCodeBehindControlResolvesTheProseTokenForItsForeground()
    {
        // ControlStyles.xaml names two controls the styles cannot reach: CaptionColorPickerWindow's
        // palette swatches and SquadLaunchWindow's Remove. Both set brushes via FindResource, so a
        // change to a Style does not reach them — which makes them the obvious place for this to
        // regrow.
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.NotNull(appDir);

        var offenders = new List<string>();

        foreach (var cs in Directory.EnumerateFiles(appDir!, "*.cs", SearchOption.AllDirectories))
        {
            if (cs.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || cs.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var lines = File.ReadAllLines(cs);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("MutedTextBrush", StringComparison.Ordinal)) continue;
                if (!lines[i].Contains("Foreground", StringComparison.Ordinal)) continue;

                // Prose built in code-behind is legitimate and there is a lot of it — seven
                // `new TextBlock` sites set this token correctly. Only a CONTROL is a violation, so
                // walk back to the nearest object being constructed and judge on that.
                var owner = NearestConstructedType(lines, i);
                if (owner is null || !ControlElements.Contains(owner)) continue;

                offenders.Add($"{Path.GetFileName(cs)}:{i + 1}  new {owner} ... {lines[i].Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A control built in code-behind may not resolve the prose token for its foreground:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheFenceSeesTheAppItClaimsTo()
    {
        // A scan that silently matches nothing passes every clause above while checking nothing.
        // ~109 bindings exist at the time of writing; the floor is deliberately far below that so
        // ordinary churn does not trip it, and far above zero so a broken scan does.
        var bindings = 0;

        foreach (var (doc, _) in AppXaml())
        {
            bindings += doc.Descendants()
                .Count(el => el.Attribute("Foreground")?.Value == ProseToken);
        }

        Assert.True(bindings >= 50,
            $"The fence found only {bindings} prose-token bindings. It is supposed to be scanning an "
            + "app with roughly 109 — a count this low means the scan is broken, not that the app "
            + "changed.");
    }
}
