using System.IO;
using System.Text.RegularExpressions;
using ROROROblox.App.History;

namespace ROROROblox.Tests;

/// <summary>
/// F-065's carrier, gated. History session rows are separated by RHYTHM — the gutter between two
/// rows exceeds the inset within one — and not by a rule.
/// <para>
/// WHY THIS FILE EXISTS AT ALL. F-098's lesson: a gate that reads markup is only ever evidence
/// about markup. These rows are built in C#, so every XAML-parsing instrument in this suite is
/// structurally blind to them, and the row separation would otherwise be the one thing this cycle
/// changed that nothing watches.
/// </para>
/// <para>
/// WHY RHYTHM AND NOT A RULE, since the plan asked for a rule. <c>ThemeSlots.InteractiveEdge</c>
/// carries the ruling: WCAG 1.4.11 governs component boundaries, not separators, and binding the
/// derived edge to a row rule "would repaint every user's theme from a hairline to mid grey to fix
/// a problem those surfaces do not have". Deriving one here measures <c>#1F3149 -> #647181</c> in
/// brand, which is that repaint. The plain <c>DividerBrush</c> bind is the alternative and measures
/// 1.05–1.16 across the four built-ins, a boundary that does not read. F-065's own fix direction
/// offers "a baseline rule OR fixed leading rhythm"; this is the second.
/// </para>
/// </summary>
public class HistoryRowRhythmTests
{
    /// <summary>
    /// THE INVARIANT. Read from the app's own constants rather than matched as text, so this checks
    /// the reason rather than the spelling — a gate matching the literal <c>12</c> would pass a
    /// change that moved both numbers the wrong way.
    /// <para>
    /// It was inverted before v1.21 item 3: a 6px gutter against a 10px inset, which argues by
    /// proximity that a row's own first line belongs to the row above it.
    /// </para>
    /// </summary>
    [Fact]
    public void TheGutterBetweenRowsExceedsTheInsetWithinOne()
    {
        Assert.True(SessionHistoryPage.RowGutter > SessionHistoryPage.RowVerticalInset,
            $"History row gutter is {SessionHistoryPage.RowGutter} and the inset within a row is "
            + $"{SessionHistoryPage.RowVerticalInset}. The gutter has to be the larger of the two "
            + "or the whitespace argues that a row's first line belongs to the row above it. This "
            + "relationship IS F-065's fix: the fill carries nothing (RowBg against the page field "
            + "measures 1.08–1.33 across the four built-ins), so the geometry is what separates two "
            + "sessions, and unlike a colour it cannot fail a theme somebody writes later.");
    }

    /// <summary>
    /// The row is a card, and in this app a card's border means STATE. On the account list a
    /// resting row has fill, radius and a gutter and no edge; an edge appears to say something —
    /// expired takes <c>RowExpiredAccent</c> at 1px, focused takes <c>Cyan</c> at 2px.
    /// <para>
    /// Pinned because "just give the row a rule, it looks cleaner" is a very easy edit to make, and
    /// it is the same edit <c>InteractiveEdgeBindingTests</c> exists to stop one layer up. A
    /// resting border here would also make History the one list that says "state" when it means
    /// "row", in the cycle whose thesis is that the surfaces get one vocabulary.
    /// </para>
    /// </summary>
    [Fact]
    public void TheRestingRowDrawsNoBoundary()
    {
        var initializer = RowBorderInitializer();

        var offenders = new[] { "BorderBrush", "BorderThickness" }
            .Where(p => initializer.Contains(p, StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"The History row's Border initializer sets {string.Join(" and ", offenders)}. A resting "
            + "row draws no boundary in this app — an edge on a row card is how the account list "
            + "says 'expired' or 'focused', and spending that channel on rows in no state makes "
            + "History the one list where an edge means nothing. If this is a deliberate STATE "
            + "border, update this gate on purpose rather than deleting it; if it is a separator, "
            + "read ThemeSlots.InteractiveEdge first — 1.4.11 does not govern separators, and the "
            + "derived edge repaints every user's authored theme.\n\nInitializer:\n" + initializer);
    }

    /// <summary>
    /// Vacuity floor for the clause above. "Found no BorderBrush" is also what a broken scan
    /// returns, and this one locates a method body by text.
    /// </summary>
    [Fact]
    public void TheScanFindsTheRowItClaimsTo()
    {
        var initializer = RowBorderInitializer();

        Assert.Contains("RowGutter", initializer, StringComparison.Ordinal);
        Assert.Contains("RowVerticalInset", initializer, StringComparison.Ordinal);
        Assert.Contains("RowBgBrush", initializer, StringComparison.Ordinal);
    }

    /// <summary>
    /// The <c>var border = new HistoryRowPresenter { … };</c> initializer inside <c>BuildRow</c>.
    /// <para>
    /// Re-pointed 2026-08-21 by F-072, which changed the row's type from <c>Border</c> to a
    /// <c>HistoryRowPresenter</c> subclass so the row could carry an automation peer. This gate
    /// caught the change immediately and its own message asked to be re-pointed rather than
    /// re-derived, which is what happened — the CLAIM it makes is unchanged, only the locator moved.
    /// A gate that silently stopped matching would have gone green while measuring nothing.
    /// </para>
    /// Scoped to the
    /// initializer rather than the file on purpose: the row also builds a Bookmark button, and a
    /// BUTTON's boundary is a component boundary that 1.4.11 does govern, so a file-wide ban on
    /// <c>BorderBrush</c> would be asserting the opposite of the rule this file is about.
    /// </summary>
    private static string RowBorderInitializer()
    {
        var appDir = XamlStyleScanner.AppSourceDirectory();
        Assert.NotNull(appDir);

        var path = Path.Combine(appDir!, "History", "SessionHistoryPage.xaml.cs");
        Assert.True(File.Exists(path), $"{path} not found — this gate would pass vacuously.");

        var source = File.ReadAllText(path);

        var match = Regex.Match(source, @"var\s+border\s*=\s*new\s+HistoryRowPresenter\s*\{(?<body>.*?)\};",
            RegexOptions.Singleline);

        Assert.True(match.Success,
            "Could not find `var border = new HistoryRowPresenter { … };` in SessionHistoryPage.BuildRow. The "
            + "row construction was restructured; re-point this gate rather than assuming it still "
            + "measures the row.");

        return match.Groups["body"].Value;
    }
}
