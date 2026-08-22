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
    // PasswordBox added 2026-08-21. Its absence was a blind spot with teeth: the three unnamed
    // controls it hid are the passphrase fields on account export and import — the most sensitive
    // inputs in the app, and the ones where "which box am I typing in" matters most.
    private static readonly string[] Kinds =
        ["Button", "ToggleButton", "ComboBox", "TextBox", "CheckBox", "PasswordBox"];

    /// <summary>
    /// MUST NOT RISE. Lower it whenever you name something; that is the point.
    /// <para>
    /// <b>ONE, and it is deliberate.</b> The survivor is <c>FriendFollowWindow</c>'s
    /// <c>SourceSwitchButton</c>, whose <c>Content</c> is assigned in code-behind —
    /// <c>$"View {ChromeName(other)}'s friends"</c>. It IS named at runtime, and better than a
    /// constant could name it; setting <c>AutomationProperties.Name</c> here would OVERRIDE an
    /// accurate label with a stale one. A scanner reading XAML cannot see that, so the honest
    /// ceiling is one rather than a lie of zero.
    /// </para>
    /// <para>
    /// <b>THE NUMBER THIS REPLACED WAS MOSTLY THE SCANNER.</b> It stood at 49, and 26 of those were
    /// artifacts of how this file measured rather than anything a person could hear: 15 controls
    /// labelled by child content the scanner only credited as an attribute (UI Automation on the
    /// running Settings window reported every one of them named), and 7 property-element tags —
    /// <c>&lt;Button.Style&gt;</c>, <c>&lt;ComboBox.ItemContainerStyle&gt;</c> — that are not
    /// controls at all and could never have been named. Two years of this row would have been spent
    /// chasing a regex. Fix the instrument before working the number it reports.
    /// </para>
    /// </summary>
    private const int UnnamedCeiling = 1;

    /// <summary>
    /// Guards the walk itself. If the regex stops matching, a broken scan would report a perfectly
    /// named app forever — the same vacuity trap the colour-literal clause keeps a floor for.
    /// </summary>
    private const int ScannedFloor = 120;

    private static (int Named, List<string> Unnamed) Scan()
    {
        var named = 0;
        var unnamed = new List<string>();
        // (?=[\s/>]) rather than \b, corrected 2026-08-21 with the child-content rule above.
        // \b matches between "Button" and the dot in `<Button.Style>`, so every property-element
        // tag in the app — `<Button.Style>`, `<Button.ToolTip>`, `<ComboBox.ItemContainerStyle>` —
        // scanned as a nameless control with an attribute list of ".Style". MainWindow alone has
        // ten. They are not controls, they cannot carry a name, and no amount of work on the app
        // could ever have removed them from the count.
        var pattern = new Regex(
            @"<(?:ui:)?(" + string.Join("|", Kinds) + @")(?=[\s/>])([^>]*?)/?>", RegexOptions.Singleline);

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
                // !Contains('{') because `Content="{Binding ActionLabel}"` is letters, and letters
                // were the whole test. A bound Content is an OBJECT at runtime, and
                // ButtonAutomationPeer names a button from its content only when that content is a
                // string — so the bound ones announce nothing while scanning as labelled.
                var literalText = content.Success
                    && !content.Groups[1].Value.Contains('{')
                    && Regex.IsMatch(content.Groups[1].Value, "[A-Za-z]{2}");

                // AND SO IS CHILD CONTENT, which this scanner used to miss entirely (F-052, corrected
                // 2026-08-21). Half the app writes `<CheckBox ...><TextBlock Text="…" /></CheckBox>`
                // rather than `Content="…"`, and WPF derives the accessible name from that child just
                // as happily. MEASURED, not reasoned: UI Automation on the running Settings window
                // reports every one of those checkboxes named — "Start RoRoRo when Windows starts."
                // — while this scanner counted them as bare. The row's headline number was a
                // property of the regex, not of the app.
                // TWO FENCES ON THIS CREDIT, both measured on the running app rather than reasoned.
                //
                // ONLY CONTENT CONTROLS. A ComboBox's inner XAML is its items and templates, and WPF
                // derives no name from them — the eight account game-pickers proved it, sitting
                // silent at runtime while this rule scored them labelled.
                //
                // AND ONLY LITERAL TEXT. `Text="{Binding DisplayName}"` contains letters, so a naive
                // letters-anywhere check reads a binding as a label. It is not one: when Content is
                // an object with a template rather than a string, ButtonAutomationPeer returns no
                // name at all. That is 62 buttons on the main window showing an account name and
                // announcing nothing — the chips a screen-reader user cannot tell apart.
                var namesFromContent = m.Groups[1].Value is "Button" or "ToggleButton" or "CheckBox";
                if (!literalText && !hasName && namesFromContent
                    && !m.Value.TrimEnd().EndsWith("/>", StringComparison.Ordinal))
                {
                    var close = text.IndexOf($"</{m.Groups[1].Value}>", m.Index, StringComparison.Ordinal);
                    if (close > 0)
                    {
                        var inner = text[(m.Index + m.Length)..close];
                        var attrText = Regex.Match(inner, @"Text\s*=\s*""([^""]*)""");
                        literalText =
                            (attrText.Success && !attrText.Groups[1].Value.Contains('{')
                                && Regex.IsMatch(attrText.Groups[1].Value, "[A-Za-z]{2}"))
                            || Regex.IsMatch(Regex.Replace(inner, "<[^>]*>", ""), "[A-Za-z]{2}");
                    }
                }

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
