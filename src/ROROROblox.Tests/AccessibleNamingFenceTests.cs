using System.IO;
using System.Text.RegularExpressions;

namespace ROROROblox.Tests;

/// <summary>
/// F-052. An interactive control that carries no accessible name is invisible to everything that
/// is not a pair of eyes.
/// <para>
/// The row asked for a naming RULE, and a rule with 49 existing violations cannot be a hard gate on
/// day one without either a 49-control rewrite or an exemption list nobody will read. So this is a
/// ceiling, the same shape <c>ThemedStatusColourTests</c> uses for colour literals: the number may
/// fall and may never rise. New controls have to be named; the backlog gets paid down whenever
/// someone is in the file anyway.
/// </para>
/// <para>
/// RE-MEASURED, and the row was stale in both directions. It says the grep "returns zero" and
/// counts "70 unnamed Buttons, 16 unnamed ComboBoxes" — 86. Today 111 controls carry a name or
/// literal text and 49 do not, so earlier waves named a great many without anyone recording it.
/// </para>
/// </summary>
public class AccessibleNamingFenceTests
{
    /// <summary>Interactive kinds — the ones a person operates, so the ones a name is load-bearing for.</summary>
    private static readonly string[] Kinds = ["Button", "ToggleButton", "ComboBox", "TextBox", "CheckBox"];

    /// <summary>
    /// Measured 2026-08-20. MUST NOT RISE. Lower it whenever you name something; that is the point.
    /// </summary>
    private const int UnnamedCeiling = 49;

    /// <summary>
    /// Guards the walk itself. If the regex stops matching, a broken scan would report a perfectly
    /// named app forever — the same vacuity trap the colour-literal clause keeps a floor for.
    /// </summary>
    private const int ScannedFloor = 120;

    private static (int Named, List<string> Unnamed) Scan()
    {
        var named = 0;
        var unnamed = new List<string>();
        var pattern = new Regex(@"<(?:ui:)?(" + string.Join("|", Kinds) + @")\b([^>]*?)/?>", RegexOptions.Singleline);

        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            var text = File.ReadAllText(file.FullPath);
            foreach (Match m in pattern.Matches(text))
            {
                var attrs = m.Groups[2].Value;
                var hasName = attrs.Contains("AutomationProperties.Name", StringComparison.Ordinal);

                // Literal text content IS an accessible name — WPF exposes it as one. A glyph or a
                // binding is not, which is exactly the distinction the row draws.
                var content = Regex.Match(attrs, @"Content\s*=\s*""([^""]*)""");
                var literalText = content.Success && Regex.IsMatch(content.Groups[1].Value, "[A-Za-z]{2}");

                if (hasName || literalText) named++;
                else unnamed.Add($"{file.Label}: {m.Groups[1].Value}");
            }
        }
        return (named, unnamed);
    }

    [Fact]
    public void TheNumberOfUnnamedInteractiveControlsNeverRises()
    {
        var (named, unnamed) = Scan();

        Assert.True(named + unnamed.Count >= ScannedFloor,
            $"The naming scan matched only {named + unnamed.Count} controls, under the {ScannedFloor} floor. "
            + "That means the walk broke, not that the app shed its controls.");

        Assert.True(unnamed.Count <= UnnamedCeiling,
            $"{unnamed.Count} interactive controls carry no accessible name, up from the {UnnamedCeiling} "
            + "measured on 2026-08-20. A Button, ToggleButton, ComboBox, TextBox or CheckBox whose "
            + "Content is a glyph, an icon or a binding needs AutomationProperties.Name — literal text "
            + "content already counts, so this only bites where a sighted user is reading a shape.\n  "
            + string.Join("\n  ", unnamed.Except(unnamed.Take(unnamed.Count - 12)).Take(12)));
    }

    [Fact]
    public void TheCeilingIsNotSlackerThanReality()
    {
        // Slack in a ceiling is room for a new offender to arrive unnoticed — the standing rule this
        // repo already applies to its colour-literal allow-list. If naming work drops the count, the
        // constant comes down with it in the same commit.
        var (_, unnamed) = Scan();

        Assert.True(unnamed.Count == UnnamedCeiling,
            $"The ceiling says {UnnamedCeiling} and the app has {unnamed.Count}. If you named some, "
            + "lower the constant in the same commit; a ceiling above reality stops being a fence.");
    }
}
