using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// Keeps the prose token off control labels (F-032).
/// <para>
/// <c>MutedTextBrush</c> does two jobs. On helper text, empty states and chips it is correct and
/// there are ~104 of those. On a control's label it is the defect: muted-vs-white measures 2.42:1
/// in the brand theme, so a control's label and the paragraph beside it are separated by almost
/// nothing. F-031 already shipped the affordance that does the separating —
/// <c>InteractiveEdgeBrush</c>, derived to clear 3:1 under any theme.
/// </para>
/// <para>
/// CORRECTED for v1.17. This doc cited "1.00:1 under flatline." That number belongs to
/// <c>flatline-lab</c>, the adversarial fixture in <c>FlatlineLabGateTests</c> — not to the
/// <c>flatline</c> theme now shipping in <c>ThemeStore</c>, which measures muted-vs-white at
/// <b>2.65:1</b>, better separation than brand's 2.42:1. The fence's rationale is unchanged by
/// that: brand is the weakest shipped separation and brand is the default, and the argument was
/// never "one theme collapses" but "colour was carrying a job the edge already does." Worth
/// knowing that neither number is watched — since PR #100 no declared pair puts the prose token
/// on a fill, so <c>ContrastPairGateTests</c> cannot see this token at all (F-086). This fence
/// guards the ROLE; nothing currently guards the RATIO.
/// </para>
/// <para>
/// This is a role fence, not a token ban. Prose keeps the token. What it may not do is label a
/// control.
/// </para>
/// <para>
/// WHERE THE FENCE CANNOT SEE. All four clauses cover element-attribute bindings on a control
/// element (a <c>Foreground="{DynamicResource MutedTextBrush}"</c> read off the control itself by
/// clause 1) and control-style setters (a <c>&lt;Setter Property="Foreground"&gt;</c> inside a
/// <c>&lt;Style TargetType&gt;</c> that resolves to a control type, by clause 2). Neither reaches a
/// control whose label colour arrives via a <b>child element</b> instead of the control's own
/// attribute or its own style. The main-account star (<c>MainWindow.xaml</c>, the
/// <c>&lt;Button&gt;</c> around line 667 whose entire content is a child <c>&lt;TextBlock&gt;</c>)
/// set its colour through a <c>&lt;Style TargetType="TextBlock"&gt;</c> nested inside that child, at
/// line 707: clause 1 finds no Foreground attribute on the Button itself, clause 2 skips the
/// nested style because <c>TextBlock</c> is not in <c>ControlElements</c>, clause 3 never opens
/// XAML, and clause 4 counted the binding as ordinary prose. That is the instance that revealed
/// the limit, and it was fixed by hand. The <c>☰</c> drag grip (<c>MainWindow.xaml</c>, the child
/// <c>&lt;TextBlock&gt;</c> around line 337 inside an interactive <c>&lt;Border&gt;</c>) is the
/// other known instance of a control's label living on a child element — there the child sets
/// Foreground directly rather than through a nested style, but the child itself is still not a
/// control by type or behaviour, so clause 1's own <c>IsControl</c> check never looks at it either.
/// It is deliberately left as-is — out of F-032's scope. Neither instance will trip this fence if
/// it regresses further; that is a known gap, not a false sense of coverage.
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

        // Candidate set for THIS clause's own vacuity floor below. TheFenceSeesTheAppItClaimsTo
        // scans element Foreground attributes only — a <Setter Property="Foreground" .../> has no
        // Foreground attribute of its own, so it never counts toward that floor. Without a floor
        // scoped to what this clause actually walks, a break in targetName extraction (say a future
        // TargetType="{x:Type ui:Foo}" form failing to resolve) silently regresses to
        // offenders.Count == 0 and this clause passes forever while checking nothing.
        var candidates = 0;

        foreach (var (doc, label) in AppXaml())
        {
            foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
            {
                var target = style.Attribute("TargetType")?.Value ?? "";

                // Matches the last identifier before end-of-string, optionally followed by a
                // closing '}' and whitespace. That covers every form the codebase writes:
                // "Button", "ui:ToggleSwitch" (namespace prefix), and "{x:Type Button}" — the
                // form every BasedOn already uses, and the one a future implicit style could use
                // for TargetType itself. The prior Split('.', ':').Last().Trim('}', ' ') left
                // "{x:Type Button}" as "Type Button" (Trim only strips from the ends, not the
                // "x:Type " prefix in the middle), which is in no ControlElements list, so that
                // style silently skipped this clause — and the floor below still counted, so
                // nothing complained.
                var targetMatch = System.Text.RegularExpressions.Regex.Match(target, @"[A-Za-z_][A-Za-z0-9_]*(?=\}?\s*$)");
                var targetName = targetMatch.Success ? targetMatch.Value : "";
                if (!ControlElements.Contains(targetName)) continue;

                foreach (var setter in style.Descendants().Where(e => e.Name.LocalName == "Setter"))
                {
                    if (setter.Attribute("Property")?.Value != "Foreground") continue;
                    candidates++;

                    if (setter.Attribute("Value")?.Value == ProseToken)
                    {
                        offenders.Add(
                            $"{label}:{LineOf(setter)} Style TargetType={targetName} sets Foreground={ProseToken}");
                    }
                }
            }
        }

        // Measured 2026-08-09: 10 Foreground setters inside control-targeted <Style> elements
        // (ControlStyles.xaml, MainWindow.xaml, PreferencesWindow.xaml — most legitimately bind
        // WhiteBrush or another named brush; two are this clause's real offenders). Floor of 5
        // leaves generous headroom below 10 for ordinary style churn while staying far above zero.
        Assert.True(candidates >= 5,
            $"This clause examined only {candidates} control-style Foreground setters. It is "
            + "supposed to be walking roughly ten — a count this low means targetName "
            + "extraction or the Style/Setter walk is broken, not that styles were removed.");

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

        // Candidate set for THIS clause's own vacuity floor below. TheFenceSeesTheAppItClaimsTo
        // never opens a .cs file at all, so a break in the directory walk or the
        // MutedTextBrush+Foreground co-occurrence scan below has nothing else watching it.
        //
        // What this floor does NOT cover: NearestConstructedType. candidates++ happens on the
        // co-occurrence match, before the discriminator ever runs — so a regression inside
        // NearestConstructedType changes which lines land in `offenders`, never how many land in
        // `candidates`. That is the exact shape of the bug caught during Task 2's own review,
        // where the discriminator matched `new Thickness(...)` before reaching `new Button` and
        // this clause silently found 0 offenders while checking nothing, with `candidates`
        // sitting unchanged the whole time. A floor over app content structurally cannot see
        // that failure, so the discriminator carries its own direct tests instead — see
        // NearestConstructedType_ResolvesTheAllmanOwner_NotAValueTypeOnAnEarlierLine below.
        var candidates = 0;

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
                candidates++;

                // Prose built in code-behind is legitimate and there is a lot of it — seven
                // `new TextBlock` sites set this token correctly. Only a CONTROL is a violation, so
                // walk back to the nearest object being constructed and judge on that.
                var owner = NearestConstructedType(lines, i);
                if (owner is null || !ControlElements.Contains(owner)) continue;

                offenders.Add($"{Path.GetFileName(cs)}:{i + 1}  new {owner} ... {lines[i].Trim()}");
            }
        }

        // Measured 2026-08-09: 9 lines where MutedTextBrush and Foreground co-occur in code-behind
        // (8 legitimate `new TextBlock` prose sites + the 1 `new Button` offender this clause
        // exists to catch). The next task fixes that one site, which drops the co-occurrence count
        // to 8 — Foreground stops naming MutedTextBrush there at all, it does not just stop being an
        // offender. Floor of 5 survives that drop with headroom to spare while staying far above
        // zero.
        Assert.True(candidates >= 5,
            $"This clause examined only {candidates} code-behind lines mentioning MutedTextBrush "
            + "and Foreground together. It is supposed to be walking roughly eight or nine — a "
            + "count this low means the directory walk or the co-occurrence scan is broken, not "
            + "that the app changed.");

        Assert.True(offenders.Count == 0,
            "A control built in code-behind may not resolve the prose token for its foreground:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NearestConstructedType_ResolvesTheAllmanOwner_NotAValueTypeOnAnEarlierLine()
    {
        // Direct test of the discriminator itself — see the "What this floor does NOT cover"
        // note above NoCodeBehindControlResolvesTheProseTokenForItsForeground's `candidates`
        // scan. No floor over app content can watch this helper, because the app no longer
        // contains a code-behind control whose Foreground resolves MutedTextBrush (Task 3 fixed
        // the one that did), so nothing left in app content exercises the positive path. This is
        // the verbatim shape of the SquadLaunchWindow bug: an Allman-style `new Button` several
        // lines above the Foreground line it owns, with a single-line value construction (`new
        // Thickness(...)`) in between that the unanchored regex used to grab instead.
        var lines = new[]
        {
            "var b = new Button",
            "{",
            "    Content = \"Remove\",",
            "    Padding = new Thickness(10, 6, 10, 6),",
            "    Foreground = (Brush)FindResource(\"MutedTextBrush\"),",
        };

        var owner = NearestConstructedType(lines, 4);

        Assert.Equal("Button", owner);
    }

    [Fact]
    public void NearestConstructedType_ResolvesTextBlockOwner_ForLegitimateProseSites()
    {
        // Negative case for the same discriminator: seven legitimate code-behind prose sites
        // depend on this returning a non-control so NoCodeBehindControlResolvesTheProseTokenFor
        // ItsForeground does not flag them.
        var lines = new[]
        {
            "var t = new TextBlock",
            "{",
            "    Text = \"Loaded\",",
            "    Foreground = (Brush)FindResource(\"MutedTextBrush\"),",
        };

        var owner = NearestConstructedType(lines, 3);

        Assert.Equal("TextBlock", owner);
    }

    [Fact]
    public void TheFenceSeesTheAppItClaimsTo()
    {
        // Backstops NoControlLabelBindsTheProseToken specifically — same scan idiom, element
        // Foreground attributes only. It does NOT reach the other two clauses: a <Setter
        // Property="Foreground" .../> has no Foreground attribute of its own (clause 2), and this
        // never opens a .cs file (clause 3). Those two now carry their own candidate-count floors,
        // next to their own offender lists, for exactly that reason — a scan that silently matches
        // nothing passes its clause while checking nothing, and this test only ever guarded one of
        // the three clauses that failure mode can strike. ~104 bindings exist at the time of
        // writing; the floor is deliberately far below that so ordinary churn does not trip it, and
        // far above zero so a broken scan does.
        var bindings = 0;

        foreach (var (doc, _) in AppXaml())
        {
            bindings += doc.Descendants()
                .Count(el => el.Attribute("Foreground")?.Value == ProseToken);
        }

        Assert.True(bindings >= 50,
            $"The fence found only {bindings} prose-token bindings. It is supposed to be scanning an "
            + "app with roughly 104 — a count this low means the scan is broken, not that the app "
            + "changed.");
    }
}
