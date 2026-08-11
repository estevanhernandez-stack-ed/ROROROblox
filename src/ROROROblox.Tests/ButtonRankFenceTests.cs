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
