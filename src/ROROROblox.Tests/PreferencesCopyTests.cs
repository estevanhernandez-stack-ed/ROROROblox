using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ROROROblox.Tests;

/// <summary>
/// Every setting on the Settings page speaks in one voice (F-043, F-062, spec §6, prd Story 4.1).
/// <para>
/// WHAT BROKE. The page spoke in three grammatical persons at once. Six checkbox labels at audit:
/// three second-person imperative, three first person — <c>"Launch my main account…"</c>,
/// <c>"Show what I'm playing on Discord."</c>, <c>"Let friends join my server from Discord"</c>,
/// the last of those also missing its terminal period. Alongside them the first helper line of the
/// first card read <c>"Adds a value under HKCU Run. Removes it when unchecked."</c> — a registry
/// path handed to a non-technical clan audience as their first read of Settings (F-062). First
/// person is a different speaker from the rest of the app's "you", and nothing failed on it.
/// </para>
/// <para>
/// WHAT THIS COVERS. Exactly two shapes of copy in
/// <c>Preferences/PreferencesWindow.xaml</c>, both of them the app speaking TO the user:
/// </para>
/// <list type="bullet">
/// <item><b>Checkbox labels</b> — a <c>TextBlock</c> whose parent is a <c>CheckBox</c>.</item>
/// <item><b>Hints</b> — a <c>TextBlock</c> bound to <c>MutedTextBrush</c>, the prose under a
/// control.</item>
/// </list>
/// <para>
/// Three rules over those: no first-person pronoun, a terminal period on every one, and no hint
/// that merely restates the label above it. A fourth asserts the page as a whole addresses the
/// reader as "you", and a fifth is the vacuity floor — a scan that matches nothing passes every
/// rule above while checking nothing, which is the <c>--filter "Foo*"</c> lesson in a different
/// costume and the reason every fence in this project carries one.
/// </para>
/// <para>
/// WHAT THIS DOES NOT COVER, and why each exclusion is a decision rather than an oversight.
/// </para>
/// <list type="number">
/// <item><b>Status messages composed in code.</b> <c>AlertStatusLine</c>,
/// <c>ThemeStatusSummary</c>, <c>MutedAccountsSummary</c> and <c>AutomaticMemorySummary</c> all
/// produce sentences, and they are already in voice — but they are not in this markup, they are
/// assembled from live state, and each already carries its own test file where its copy is
/// asserted against the values that produced it. A rule written here could only read the empty
/// <c>TextBlock</c> they write into.</item>
/// <item><b>Field labels</b> — the SemiBold or plain-white line naming the control under it
/// ("Idle warn threshold", "Memory to keep free (MB)", "An account drops out"). They name a field;
/// they are not sentences, so the terminal-period rule would be wrong on them. They still anchor
/// rule 3 below, because a hint may not restate one.</item>
/// <item><b>Button and <c>ComboBoxItem</c> content.</b> Two separate reasons, and the first is
/// load-bearing: a button can legitimately speak in the user's OWN first person, because pressing
/// it is the user saying the thing. <c>Content="I don't have a Discord server"</c> is correct
/// copy, and a blanket first-person ban that reached button labels would have to special-case it —
/// an exemption for the one string that is right. Second, "Off" / "Desktop only" / "My channel" /
/// "Clan channel" is a paired vocabulary of destination NAMES, not a voice: "My channel" means the
/// server that is yours as against the clan's, its <c>Tag="Mine"</c> is what persists, and
/// <c>AlertStatusLine</c> composes messages around that name. So <b>"My channel" is the one
/// first-person string left on this page, deliberately, and this test does not reach it.</b>
/// Renaming it is a routing-vocabulary decision, not a copy pass.</item>
/// <item><b>Semantic restatement.</b> Rule 3 catches a hint that repeats its label's WORDS. It
/// cannot see one that repeats its label's MEANING in a different vocabulary, which is precisely
/// what F-062's first clause did: the 2026-08-10 re-verification records that "Adds a value under
/// HKCU Run" and "Start RoRoRo when Windows starts" share no wording at all, so the row's
/// "duplicates the checkbox label" sub-claim never held textually. That line was fixed by hand and
/// this file cannot prove the next one. A rule that tried would need to judge meaning, and a copy
/// test with false positives gets weakened later, which makes it worse than nothing.</item>
/// <item><b>Whether the copy is any good.</b> Jargon, hedging, "seamlessly", em-dash pile-ups and
/// register are all read by a human aloud, per this cycle's own Verify step. Green here means the
/// page has one speaker, not that it reads well.</item>
/// </list>
/// </summary>
public class PreferencesCopyTests
{
    private const string PagePath = "src/ROROROblox.App/Preferences/PreferencesWindow.xaml";

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private const string ProseToken = "{DynamicResource MutedTextBrush}";
    private const string HeadingStyle = "SectionHeadingStyle";

    private enum Kind
    {
        /// <summary>A <c>TextBlock</c> inside a <c>CheckBox</c>. The setting's own name.</summary>
        CheckBoxLabel,

        /// <summary>Prose under a control, bound to the muted token.</summary>
        Hint,

        /// <summary>Names the control below it. Anchors rule 3; excluded from rules 1, 2 and 4.</summary>
        FieldLabel,

        /// <summary>A <c>SectionHeadingStyle</c> heading. Anchors rule 3 only.</summary>
        SectionHeading,
    }

    private sealed record Line(string Page, int Number, Kind Kind, string Text)
    {
        public override string ToString() => $"{PagePath}:{Number} ({Page}, {Kind})  \"{Text}\"";
    }

    /// <summary>
    /// Every <c>Text</c>-bearing <c>TextBlock</c> inside the five page panels, in document order,
    /// classified. The classification is TOTAL — anything that is not a checkbox label, a heading
    /// or muted prose is a field label — so no string on the page can fall through a gap unnoticed.
    /// Status lines (<c>AlertsStatusLine</c>, <c>ThemeStatusLine</c>, <c>MemorySettingsWarning</c>,
    /// <c>MutedAccountsLine</c>, <c>AutomaticMemoryLine</c>) declare no <c>Text</c> attribute at
    /// all — the code-behind fills them — so they are absent here by construction rather than by
    /// exclusion.
    /// </summary>
    private static IReadOnlyList<Line> PageCopy()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        if (root is null) return [];

        var path = Path.Combine(root, PagePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return [];

        var doc = XDocument.Load(path, LoadOptions.SetLineInfo);
        var lines = new List<Line>();

        // The rail's five pages, named PageStartup / PageAccounts / PageAlerts / PageDiscord /
        // PageAppearance. Scoping to them keeps the window's own chrome (the nav rail, the Close
        // button) out of a rule about settings copy.
        var pages = doc.Descendants()
            .Where(e => e.Name.LocalName == "StackPanel"
                        && (e.Attribute(Xaml + "Name")?.Value ?? "").StartsWith("Page", StringComparison.Ordinal));

        foreach (var page in pages)
        {
            var pageName = page.Attribute(Xaml + "Name")!.Value;

            foreach (var el in page.DescendantsAndSelf().Where(e => e.Name.LocalName == "TextBlock"))
            {
                var text = el.Attribute("Text")?.Value;
                if (string.IsNullOrWhiteSpace(text)) continue;

                var kind =
                    el.Parent?.Name.LocalName == "CheckBox" ? Kind.CheckBoxLabel
                    : (el.Attribute("Style")?.Value ?? "").Contains(HeadingStyle, StringComparison.Ordinal) ? Kind.SectionHeading
                    : el.Attribute("Foreground")?.Value == ProseToken ? Kind.Hint
                    : Kind.FieldLabel;

                var number = el is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;
                lines.Add(new Line(pageName, number, kind, text));
            }
        }

        return lines;
    }

    /// <summary>The two shapes this file judges: the app speaking to the user.</summary>
    private static bool IsSettingsCopy(Line line) =>
        line.Kind is Kind.CheckBoxLabel or Kind.Hint;

    /// <summary>
    /// First person, in every contracted form the page could write. Both the straight apostrophe
    /// and U+2019 are covered, because a later editor pasting from a word processor must not be
    /// able to smuggle "I'm" back in.
    /// <para>
    /// The leading lookbehind is what keeps "your" from matching "our" — the single most likely
    /// false positive on a page whose whole voice is the second person, and one that would fire on
    /// twenty legitimate lines at once.
    /// </para>
    /// </summary>
    private static readonly Regex FirstPerson = new(
        @"(?<![A-Za-z'’])(?:I['’](?:m|ve|ll|d)|we['’](?:re|ve|ll|d)|ourselves|myself|mine|ours|our|us|we|my|me|I)(?![A-Za-z'’])",
        RegexOptions.Compiled);

    /// <summary>
    /// Second person, the voice the whole page is supposed to be in. Case-insensitive because a
    /// sentence can open with "You".
    /// </summary>
    private static readonly Regex SecondPerson = new(
        @"(?<![A-Za-z'’])you(?:['’](?:re|ve|ll|d))?|(?<![A-Za-z'’])your(?:s|self)?(?![A-Za-z'’])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [Fact]
    public void NoSettingSpeaksInTheFirstPerson()
    {
        // Case-SENSITIVE on purpose for the pronoun "I", which is the only one whose lowercase form
        // is a common word fragment. Every other alternative in the pattern is lowercase, and the
        // page writes sentence case, so a capitalised "My" at the start of a hint is caught by the
        // dedicated clause below rather than by making the whole pattern case-insensitive and
        // dragging "I" along with it.
        var offenders = PageCopy()
            .Where(IsSettingsCopy)
            .Where(l => FirstPerson.IsMatch(l.Text))
            .Select(l => $"{l}  → first person: \"{FirstPerson.Match(l.Text).Value}\"")
            .ToList();

        // The same rule again for a sentence-initial possessive, which the case-sensitive pattern
        // above would miss.
        offenders.AddRange(PageCopy()
            .Where(IsSettingsCopy)
            .Where(l => Regex.IsMatch(l.Text, @"^(?:My|Our|We|Me|Mine)\b"))
            .Select(l => $"{l}  → opens in the first person"));

        Assert.True(offenders.Count == 0,
            "A setting on this page speaks as \"I\" or \"my\". The app addresses the user as "
            + "\"you\" everywhere else, so a first-person label is a second speaker on one page "
            + "(F-043, prd Story 4.1). Rewrite it in the second person — \"Launch your main "
            + "account…\", \"Show what you're playing…\", \"Let friends join your server…\":\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryLabelAndHintEndsInAPeriod()
    {
        // Scoped away from field labels deliberately: "Memory to keep free (MB)" names a box, it
        // does not say anything, and a period on it would be wrong. See the class doc.
        var offenders = PageCopy()
            .Where(IsSettingsCopy)
            .Where(l => !l.Text.TrimEnd().EndsWith('.'))
            .Select(l => l.ToString())
            .ToList();

        Assert.True(offenders.Count == 0,
            "A checkbox label or hint on this page does not end in a period. One setting stopping "
            + "mid-sentence while the eight beside it do not is the same defect as a change of "
            + "person: the page stops sounding like one app (prd Story 4.1):\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void NoHintRestatesTheLabelAboveIt()
    {
        // Pairs each hint with the nearest label ABOVE it in document order — a checkbox label, a
        // field label, or the section heading when the hint introduces a whole section and has no
        // control of its own (the Alerts intro, the Accounts transport line, the Theme blurb). That
        // makes the rule total: every hint on the page has an anchor.
        var offenders = new List<string>();
        Line? anchor = null;
        var pairsChecked = 0;

        foreach (var line in PageCopy())
        {
            if (line.Kind is not Kind.Hint)
            {
                anchor = line;
                continue;
            }

            if (anchor is null || anchor.Page != line.Page) continue;
            pairsChecked++;

            var labelWords = Words(anchor.Text);
            var opening = FirstSentence(line.Text);
            var openingWords = Words(opening);

            if (Normalise(line.Text) == Normalise(anchor.Text))
            {
                offenders.Add($"{line}  → repeats its label verbatim: \"{anchor.Text}\"");
                continue;
            }

            if (openingWords.Count > 0 && !openingWords.Except(labelWords).Any())
            {
                offenders.Add(
                    $"{line}  → its first sentence adds no word the label above does not already "
                    + $"have: \"{anchor.Text}\"");
            }
        }

        // This clause's own floor. A pairing walk that anchors nothing checks nothing.
        Assert.True(pairsChecked >= 12,
            $"Only {pairsChecked} hint/label pairs were formed. Eighteen exist today — the "
            + "document-order walk or the classification is broken, not the page.");

        Assert.True(offenders.Count == 0,
            "A hint on this page says what the label directly above it already said. The hint's "
            + "job is the part the label cannot carry — the scope, the boundary, the reassurance — "
            + "not a second pass at the same sentence (prd Story 4.1, F-062):\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void TheSettingsCopyAddressesTheReaderAsYou()
    {
        // A PAGE-LEVEL FLOOR, AND DELIBERATELY NOT A PER-LINE RULE. Requiring every setting to
        // contain "you" was written and rejected: three blocks that are already in voice would
        // fail it — LaunchMainHint ("Pair with the toggle above…", imperative, whose implied
        // subject IS the reader), the Recycle hint and the idle-alerts hint, all three of which
        // describe what RoRoRo does and are correct doing so. Rewriting good prose to satisfy a
        // regex is how a copy test earns its own deletion. The negative rule above is the half
        // that bites per line; this one exists so the page cannot drift wholesale into
        // manual-style third person while every individual line stays defensible.
        var copy = PageCopy().Where(IsSettingsCopy).ToList();
        var markers = copy.Sum(l => SecondPerson.Matches(l.Text).Count);

        Assert.True(markers >= 15,
            $"The settings copy names the reader only {markers} times. Twenty-nine today across "
            + "nine labels and eighteen hints. A count this low means the page stopped talking to "
            + "anyone — or the pattern stopped matching, which fails in the same direction and is "
            + "worth looking at first.");
    }

    [Fact]
    public void TheCopyTestSeesThePageItClaimsTo()
    {
        // Backstops every rule above. Each assertion fails in the direction a broken scan would
        // otherwise pass.
        var copy = PageCopy();

        Assert.True(copy.Count > 0,
            $"{PagePath} produced no copy at all. Repo-root discovery or the XAML load failed, and "
            + "every rule in this file passed over an empty list.");

        var pages = copy.Select(l => l.Page).Distinct().ToList();
        Assert.True(pages.Count == 5,
            $"Found {pages.Count} settings pages ({string.Join(", ", pages)}); the rail declares "
            + "five. A page renamed off the Page* convention is invisible to every rule here.");

        var labels = copy.Count(l => l.Kind == Kind.CheckBoxLabel);
        Assert.True(labels >= 7,
            $"Only {labels} checkbox labels were classified. Nine exist today, up from the six "
            + "F-043 measured at audit — items 3 and 4 added the memory watchdog and careful mode. "
            + "A count under seven means the CheckBox-parent test broke.");

        var hints = copy.Count(l => l.Kind == Kind.Hint);
        Assert.True(hints >= 12,
            $"Only {hints} hints were classified. Eighteen exist today. A count this low means the "
            + "MutedTextBrush match broke, and the terminal-period and restatement rules went "
            + "quiet with it.");

        var fields = copy.Count(l => l.Kind == Kind.FieldLabel);
        Assert.True(fields >= 5,
            $"Only {fields} field labels were classified. Eight exist today, and they are what "
            + "anchors the restatement rule for the three memory boxes and the two webhook fields.");

        // The classification is total, so nothing can sit in a gap. Prove the total actually
        // partitions rather than dumping the page into one bucket.
        Assert.Equal(copy.Count, labels + hints + fields + copy.Count(l => l.Kind == Kind.SectionHeading));

        // The first-person pattern is the one rule with a real false-positive risk, and the one
        // most likely to be quietly loosened later. Pin both directions on strings rather than on
        // the page, so it stays proven after the page stops containing an offender. Behind a local
        // function because Assert.Matches would swallow the explanations, and the explanations are
        // the point of the negative cases.
        bool SpeaksAsI(string copy) => FirstPerson.IsMatch(copy);

        Assert.True(SpeaksAsI("Launch my main account when RoRoRo starts."),
            "The three strings this cycle replaced must still be caught, or the rule proves nothing.");
        Assert.True(SpeaksAsI("Show what I'm playing on Discord."));
        Assert.True(SpeaksAsI("Let friends join my server from Discord"));
        Assert.False(SpeaksAsI("Launch your main account when RoRoRo starts."),
            "\"your\" contains \"our\" — the lookbehind that stops that match is the whole reason "
            + "this pattern is safe to run over a second-person page.");
        Assert.False(SpeaksAsI("Show what you're playing on Discord."));
        Assert.False(SpeaksAsI("Hides names, avatars, and share links inside RoRoRo."),
            "\"names\" must not match \"me\", and \"memory\" must not either.");
        Assert.False(SpeaksAsI("RoRoRo checks memory every 30 seconds and tells you."));
    }

    /// <summary>Text up to the first sentence break, or all of it when there is only one.</summary>
    private static string FirstSentence(string text)
    {
        var stop = text.IndexOf(". ", StringComparison.Ordinal);
        return stop < 0 ? text : text[..(stop + 1)];
    }

    /// <summary>
    /// Words of four letters or more, lowercased. Four rather than three because the short words
    /// are the ones two unrelated sentences share by accident — "the", "and", "you", "for" — and a
    /// restatement rule built on those would fire on prose that restates nothing.
    /// </summary>
    private static HashSet<string> Words(string text) =>
        Regex.Matches(text.ToLowerInvariant(), "[a-z]+")
            .Select(m => m.Value)
            .Where(w => w.Length >= 4)
            .ToHashSet();

    private static string Normalise(string text) =>
        Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
}
