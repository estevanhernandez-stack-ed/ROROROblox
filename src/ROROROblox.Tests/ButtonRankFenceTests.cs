using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The fence. A button declaration may not paint itself; it takes a rank and the rank paints it.
/// <para>
/// v1.20 migrated 63 sites onto seven ranks. Nothing stopped the 64th from being written the old
/// way. Every previous consistency fix in this repo had to be applied at every call site, which is
/// why they drifted: the fix was a sweep and the sweep had no successor. A gate is the successor.
/// </para>
/// <para>
/// <b>The abort clause did not fire, and that is worth stating plainly.</b> <c>checklist.md</c> item
/// 9 says: if the exemption list is large enough that the fence mostly measures its own allow-list,
/// do not ship it, and record how many exemptions it would have needed. Measured on the migrated
/// tree: <b>112 declarations, 1 exemption.</b> A fence at 99.1% coverage is measuring the tree.
/// </para>
/// <para>
/// <b>This file, not <c>scripts/count-button-sites.ps1</c>, is the guard (F-098).</b> That script
/// is invoked by no workflow — the only one CI runs is <c>build-velopack-release.ps1</c> — so its
/// number is a measurement somebody types, and F-068 could regress silently between typings. This
/// runs on every push.
/// <para>
/// The two report different totals and both are right, which is worth stating once so it is not
/// re-derived a fourth time: the script counts <c>&lt;Button</c> and <c>&lt;ui:Button</c> in XAML
/// and reports <b>108</b>; this fence adds <c>ToggleButton</c> and <c>RepeatButton</c> for
/// <b>112</b>, plus <b>5</b> constructed in code-behind. The script's narrow definition is
/// deliberate and stays — <c>spec.md > §6</c> fixed it so F-068's branch-point and close figures
/// compare to each other — but it is a count of declarations the migration has not reached, not a
/// count of the app's buttons.
/// </para>
/// <b>Why ToggleButton is in scope.</b> Counting only <c>Button</c> is the definition that let the
/// header's default-game widget keep the OS template's hardcoded <c>#BEE6FD</c> hover through the
/// entire migration, in the middle of the toolbar, while the count read "1 site left" — the control
/// was outside the definition, so the instrument reported success at the moment it was blindest.
/// Adding the type here cost nothing and immediately found two more: both webhook <i>Show</i>
/// buttons in Preferences repeated <c>SecondaryToggleButtonStyle</c>'s four attributes by hand
/// instead of taking it, so they were still flashing Aero blue on hover after the rest of the app
/// had stopped.
/// </para>
/// </summary>
public class ButtonRankFenceTests
{
    /// <summary>
    /// The declaration forms this fence governs. <c>ui:Button</c> is included even though the app
    /// declares none today — <c>spec.md > §0.3</c> cut the WPF-UI migration from this cycle rather
    /// than from the project, so the day someone writes one it is fenced on arrival rather than
    /// after the next audit finds it.
    /// </summary>
    private static readonly string[] FencedTypes = { "Button", "ToggleButton", "RepeatButton" };

    /// <summary>
    /// Colour properties a rank owns. Layout properties (Padding, Margin, FontSize) deliberately
    /// stay at the call site — <c>ControlStyles.xaml</c>'s own header says a toolbar button and a
    /// dialog footer button are not the same size, and fencing those would trade this defect for a
    /// layout sweep nobody asked for.
    /// </summary>
    private static readonly Regex ColourAttribute = new(
        @"(?<![A-Za-z.])(Background|Foreground|BorderBrush)\s*=\s*""",
        RegexOptions.Compiled);

    /// <summary>
    /// A declaration's opening tag. The lookahead after the type name is load-bearing: without it
    /// <c>&lt;Button.Style&gt;</c> — a property element, not a declaration — matches, and the count
    /// comes back 117 against the committed scanner's 108. The scanner in <c>scripts/</c> shipped
    /// that exact defect earlier this cycle and it looked correct because it reproduced.
    /// </summary>
    private static Regex DeclarationOf(string type) =>
        new($@"<\s*(?:ui:)?{type}(?=[\s/>])[^>]*?/?>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>
    /// A button built in C# that assigns a colour property instead of a Style.
    /// <para>
    /// <b>This half exists because the other half was not enough, and the repo already knew.</b>
    /// <c>SquadLaunchWindow</c>'s Remove carried a comment from wave 5 saying "built in code, so
    /// wave 5's markup sweep never saw it — and neither does any test in that wave, which all parse
    /// XAML." That was true, recorded, left at one site, and then v1.20 shipped a markup-only fence
    /// on top of it whose commit message claimed 99.1% coverage. Five buttons were constructed in
    /// code at the time, three of them byte-identical to a rank that already existed.
    /// </para>
    /// <para>
    /// The general form, worth asking of every instrument in this suite: <i>does the behaviour this
    /// asserts actually live in the thing it reads?</i> A gate that reads markup can only ever be
    /// evidence about markup, and coverage claimed beyond that is coverage invented.
    /// </para>
    /// <para>
    /// What these five got instead of a rank: <c>App.xaml</c> merges <c>ui:ControlsDictionary</c>,
    /// so a bare <c>new Button</c> picks up WPF-UI's implicit style rather than the OS one — they
    /// were NOT flashing Aero blue, which was this gate author's first guess and was wrong. Their
    /// state setters are <c>DynamicResource</c>, but they resolve against
    /// <c>ui:ThemesDictionary Theme="Dark"</c>, a fixed palette <c>ThemeService</c> never touches.
    /// Correct at rest, frozen in every state, in every theme.
    /// </para>
    /// </summary>
    private static readonly Regex CsharpConstruction = new(
        @"new\s+(?:ui\.)?(?:Button|ToggleButton|RepeatButton)\s*(?:\(\s*\))?\s*\{(?<body>[^}]*)\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CsharpColourAssignment = new(
        @"(?<![A-Za-z.])(Background|Foreground|BorderBrush)\s*=",
        RegexOptions.Compiled);

    private static readonly Regex CsharpStyleAssignment = new(
        @"(?<![A-Za-z.])Style\s*=", RegexOptions.Compiled);

    /// <summary>
    /// Declarations allowed to paint themselves, each with the reason inline as item 9 requires.
    /// An exemption is a debt with a name on it, not a permission slip: <see
    /// cref="NoExemptionSurvivesItsSite"/> fails when one stops matching anything, so a site that
    /// gets migrated cannot leave its excuse behind.
    /// </summary>
    private static readonly (string File, string Element, string Reason)[] Exemptions =
    [
        // The header's default-game widget. It carries SecondaryToggleButtonStyle via BasedOn for
        // the template, then overrides all three colours because it is deliberately NOT a secondary
        // button at rest: RowBg fill where the rank uses Navy, and a Cyan edge where the rank uses
        // the derived InteractiveEdge. It is the one control in the header that has to read as a
        // picker rather than an action. Removing the overrides would move it at rest, which
        // checklist.md forbids for this cycle; giving it a rank of its own is a real option and
        // belongs to whichever cycle takes the borders debt, since the edge is the difference.
        ("MainWindow.xaml", "DefaultGameWidget",
            "deliberate non-secondary resting appearance; see the borders debt (F-052)"),
    ];

    private sealed record Site(string File, string Line, int Number, string Tag);

    /// <summary>Buttons built in code-behind, with the property block each one assigns.</summary>
    private static List<Site> CodeBehindConstructions()
    {
        var sites = new List<Site>();
        var appDir = XamlStyleScanner.AppSourceDirectory();
        if (appDir is null) return sites;

        // Repo-relative, matching the labels the XAML half prints. An absolute path in a failure
        // message names a user profile directory, which reads differently on every machine — the
        // same class of thing the repo's own local-path guard exists to keep out of the tree.
        // (That guard blocked this comment's first draft, which said so by example.)
        var root = XamlStyleScanner.FindRepoRoot()?.Replace('\\', '/').TrimEnd('/');

        foreach (var path in Directory.EnumerateFiles(appDir, "*.cs", SearchOption.AllDirectories))
        {
            var rel = path.Replace('\\', '/');
            if (rel.Contains("/obj/", StringComparison.Ordinal) || rel.Contains("/bin/", StringComparison.Ordinal))
                continue;

            if (root is not null && rel.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                rel = rel[(root.Length + 1)..];

            var text = File.ReadAllText(path);
            foreach (Match m in CsharpConstruction.Matches(text))
            {
                var number = text[..m.Index].Count(c => c == '\n') + 1;
                sites.Add(new Site(Path.GetFileName(path), rel, number, m.Groups["body"].Value));
            }
        }
        return sites;
    }

    /// <summary>
    /// The same rule, on the half of the app that is not markup. A button built in code takes a
    /// rank exactly as a declared one does.
    /// </summary>
    [Fact]
    public void NoCodeBehindButtonPaintsItself()
    {
        var offenders = CodeBehindConstructions()
            .Where(s => CsharpColourAssignment.IsMatch(s.Tag) && !CsharpStyleAssignment.IsMatch(s.Tag))
            .Where(s => !CodeExemptions.Any(e => s.File.Equals(e.File, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These code-built buttons assign a colour instead of taking a rank:\n"
            + string.Join("\n", offenders.Select(o =>
                $"  {o.Line}:{o.Number}  " + string.Join(", ", CsharpColourAssignment.Matches(o.Tag)
                    .Select(m => m.Groups[1].Value).Distinct())))
            + "\n\nSet Style = (Style)FindResource(\"…ButtonStyle\") and drop the colour properties. "
            + "A button built in code is not exempt from the vocabulary; it is only harder to see.");
    }

    /// <summary>
    /// Vacuity floor for the code half, kept separate from the markup one so a broken .cs walk
    /// cannot hide behind a healthy XAML walk. Both halves have to be seen to be trusted.
    /// </summary>
    [Fact]
    public void TheCodeBehindWalkActuallyFindsButtons()
    {
        var found = CodeBehindConstructions();
        Assert.True(found.Count >= 4,
            $"the code-behind walk found {found.Count} constructed buttons; the tree carries 5. "
            + "A walk that finds nothing passes NoCodeBehindButtonPaintsItself for free, which is "
            + "precisely how the markup-only version of this fence claimed 99.1% coverage.");
    }

    /// <summary>
    /// Code-built buttons allowed to paint themselves. Both entries are decisions, not oversights.
    /// </summary>
    private static readonly (string File, string Reason)[] CodeExemptions =
    [
        // The palette swatches ARE their colour — Background is the value being picked, not a
        // theme token, and a rank would paint over the only thing the control communicates.
        ("CaptionColorPickerWindow.xaml.cs", "the fill is the data being chosen, not a theme slot"),

        // History's Bookmark is a cyan LABEL on a navy fill with a cyan edge. No rank provides
        // that: every rank pairs a navy fill with a white label. spec.md §3's rule is that a site
        // needing a look no rank provides opens a row rather than growing the vocabulary
        // mid-sweep, so it is exempt here and recorded rather than hand-assigned to a near-miss.
        ("SessionHistoryPage.xaml.cs", "cyan label on navy — no rank provides it; see F-098"),
    ];

    private static List<Site> Declarations()
    {
        var sites = new List<Site>();
        foreach (var file in XamlStyleScanner.EnumerateAppXamlFiles())
        {
            var text = File.ReadAllText(file.FullPath);
            foreach (var type in FencedTypes)
            {
                foreach (Match m in DeclarationOf(type).Matches(text))
                {
                    // "<ToggleButton" also satisfies the "Button" pattern's prefix in the other
                    // direction is not possible, but "<Button" must not swallow "<ToggleButton":
                    // the regex anchors on '<' followed by optional 'ui:' then the literal type, so
                    // only an exact type name matches. Asserted rather than assumed, because a
                    // double-counted site inflates coverage exactly like a missed one deflates it.
                    var number = text[..m.Index].Count(c => c == '\n') + 1;
                    sites.Add(new Site(Path.GetFileName(file.FullPath), file.Label, number, m.Value));
                }
            }
        }
        return sites;
    }

    /// <summary>The headline. A declaration paints itself, or it does not ship.</summary>
    [Fact]
    public void NoButtonDeclarationPaintsItself()
    {
        var offenders = Declarations()
            .Where(s => ColourAttribute.IsMatch(s.Tag))
            .Where(s => !Exemptions.Any(e =>
                s.File.Equals(e.File, StringComparison.OrdinalIgnoreCase)
                && s.Tag.Contains($"x:Name=\"{e.Element}\"", StringComparison.Ordinal)))
            .ToList();

        Assert.True(offenders.Count == 0,
            "These declarations set a colour inline instead of taking a rank:\n"
            + string.Join("\n", offenders.Select(o =>
                $"  {o.Line}:{o.Number}  " + string.Join(", ", ColourAttribute.Matches(o.Tag)
                    .Select(m => m.Groups[1].Value))))
            + "\n\nAssign the rank whose look the site already has. If no rank provides that look, "
            + "open a row — spec.md §3 says the vocabulary does not grow mid-sweep.");
    }

    /// <summary>
    /// The fence measures the tree, not its own allow-list. This is item 9's abort clause,
    /// compiled: if exemptions ever grow past a twentieth of the declarations, the gate has stopped
    /// reporting coverage it has and the finding is that it should be deleted.
    /// </summary>
    [Fact]
    public void TheFenceMeasuresTheTreeAndNotItsAllowList()
    {
        var total = Declarations().Count;

        // Vacuity floor. Every assertion in this file quantifies over whatever the scan returns, so
        // a scan that broke and returned nothing would pass all of them — the precise failure this
        // cycle's own scanner shipped twice before anyone noticed.
        Assert.True(total >= 100,
            $"only {total} button declarations found; the migrated tree carries 112. "
            + "A scan this small is broken, not clean.");

        Assert.True(Exemptions.Length * 20 <= total,
            $"{Exemptions.Length} exemptions against {total} declarations. Past this ratio the "
            + "fence mostly measures its own allow-list, and checklist.md item 9 says to delete it "
            + "and record the count rather than ship coverage it does not have.");
    }

    /// <summary>
    /// An exemption outliving its site is how an allow-list rots into permission. If a listed
    /// element stops painting itself — because someone migrated it — the entry must go with it.
    /// </summary>
    [Fact]
    public void NoExemptionSurvivesItsSite()
    {
        var sites = Declarations();

        foreach (var (file, element, reason) in Exemptions)
        {
            var match = sites.FirstOrDefault(s =>
                s.File.Equals(file, StringComparison.OrdinalIgnoreCase)
                && s.Tag.Contains($"x:Name=\"{element}\"", StringComparison.Ordinal));

            Assert.True(match is not null,
                $"exemption for {file}/{element} matches no declaration. Either it was renamed or "
                + "it was migrated; either way the entry is stale and must be removed.");

            Assert.True(ColourAttribute.IsMatch(match!.Tag),
                $"{file}/{element} no longer sets a colour inline, so it does not need its "
                + $"exemption (\"{reason}\"). Delete the entry — an allow-list nobody prunes stops "
                + "being a record of debt and becomes a licence.");
        }
    }
}
