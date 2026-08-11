using System.Globalization;
using System.IO;
using ROROROblox.App.Theming;
using ROROROblox.Core.Theming;

namespace ROROROblox.App.Preferences;

/// <summary>
/// What the Theme section has to say, and nothing it does not.
/// <para>
/// WHY THIS EXISTS. Two failures on the Appearance page were silent. A theme that could not be
/// written down still went on screen, so the user saw exactly what a successful save looks like and
/// found the old theme back after a restart (<c>prd.md &gt; Story 3.1</c>). A theme file the store
/// could not read was dropped without a word, so a malformed JSON simply never appeared
/// (<c>prd.md &gt; Story 3.2</c>). Both are the cycle's own defect: the app knew something and did
/// not say it.
/// </para>
/// <para>
/// <b>THE WARNING VOICE, NOT THE ACCENT — the one deliberate divergence from the pattern being
/// mirrored (<c>spec.md &gt; §5</c>).</b> <c>AlertsStatusLine</c> is <c>CyanBrush</c>, and cyan is
/// the accent: the same treatment a success would get. v1.17 established
/// <c>RowExpiredAccentBrush</c> plus <see cref="WarnGlyph"/> as this app's warning vocabulary
/// across expired rows, idle chips, memory chips and the compat banner, and item 3's
/// <c>MemorySettingsWarning</c> already speaks it. This is the sixth surface and it speaks it too.
/// <c>AlertsStatusLine</c> is left exactly as it is; retro-fitting it is not this cycle's row and
/// is recorded in <c>spec.md &gt; §10</c> as an open issue.
/// </para>
/// <para>
/// Pure and window-free, following <see cref="AutomaticMemorySummary"/> and
/// <see cref="MutedAccountsSummary"/>. The suite constructs no <c>Window</c> and no
/// <c>Application</c>, so copy that lives inside a click handler is copy nothing watches.
/// </para>
/// </summary>
internal static class ThemeStatusSummary
{
    /// <summary>
    /// U+25B2 BLACK UP-POINTING TRIANGLE. Emoji_Presentation=No, so it is a Segoe UI geometric
    /// glyph rather than emoji, and it is the same codepoint <c>AccountSummary</c>'s idle chip,
    /// <c>MemoryChipFormatter</c>, the compat banner and <c>ShowMemoryWarning</c> already carry.
    /// Held here rather than reached for from another surface, which is the shape the rest of the
    /// app uses — <c>ExpiredRowRedundancyTests</c> keeps its own copy for the same stated reason,
    /// that a helper reaching across surfaces couples their failure modes. Pinned by codepoint in
    /// <c>ThemeStatusSummaryTests</c>, because the failure here is an encoding accident and
    /// mojibake still renders something a human proofing a screenshot can miss.
    /// </summary>
    internal const string WarnGlyph = "▲";

    /// <summary>
    /// How many bad files get named before the line starts summarising. A wrapping TextBlock
    /// holding forty filenames pushes the card off the page, and the fortieth name helps nobody.
    /// </summary>
    private const int MaxNamed = 5;

    /// <summary>
    /// What the line should say, and whether it should be on screen at all. <paramref name="Any"/>
    /// is what drives visibility rather than a comparison written at the call site, for the reason
    /// <see cref="MutedAccountsSummary.Summary"/> gives: a collapsed line holding stale text is one
    /// state change away from being visible again saying the wrong thing.
    /// </summary>
    internal readonly record struct Line(bool Any, string Text);

    /// <summary>Nothing to report. The success path, and the resting state of an intact folder.</summary>
    internal static Line Silent => new(false, string.Empty);

    /// <summary>
    /// What to say after a theme change, which for a change that worked is nothing.
    /// <para>
    /// SUCCESS STAYS SILENT (<c>prd.md &gt; Story 3.1</c>). A status line that speaks on every save
    /// is noise, and noise is how the one message that matters gets skipped.
    /// </para>
    /// </summary>
    internal static Line ForThemeChange(string? themeName, ThemeChange change)
    {
        var name = string.IsNullOrWhiteSpace(themeName) ? "That theme" : themeName!.Trim();

        if (!change.Found)
        {
            // Reachable by deleting a theme file while Settings is open: the picker still holds the
            // row, the store no longer holds the theme. Nothing was applied, so this must not say
            // anything is on.
            return Warn($"{name} isn't in your themes folder any more, so nothing changed. Close "
                        + "and reopen Settings to see what's there now.");
        }

        if (change.Persisted)
        {
            return Silent;
        }

        // The theme IS on. Saying so first is the point of the sentence — the session is not
        // degraded (prd.md > Story 3.1), and a user who reads only the first clause has read the
        // true part.
        var because = string.IsNullOrWhiteSpace(change.PersistError)
            ? "."
            : ": " + Terminated(change.PersistError!.Trim());

        return Warn($"{name} is on now, but RoRoRo couldn't remember it{because} You'll be back on "
                    + "your old theme the next time you start.");
    }

    /// <summary>
    /// The files in the themes folder that did not become themes, named.
    /// <para>
    /// <b>IT INFERS FROM ABSENCE, and that is sound because absence has exactly two causes.</b>
    /// <see cref="ThemeStore.ListAsync"/> drops a <c>*.json</c> file for one of two reasons and no
    /// others: <c>TryLoadFileAsync</c> returned null (malformed JSON, a missing required field, an
    /// unreadable or locked file — every throw is caught there), or the id collided with a built-in
    /// and the built-in won. Both are visible from out here without changing anything in Core: the
    /// first leaves the id absent from the list entirely, the second leaves it present and flagged
    /// <see cref="Theme.IsBuiltIn"/>. So a file on disk whose id is missing from
    /// <paramref name="loaded"/> could not be read, and the report never has to guess.
    /// </para>
    /// <para>
    /// WHY NOT ASK THE STORE. <see cref="ThemeStore"/> is in <c>ROROROblox.Core</c> and
    /// <c>spec.md &gt; §1</c> puts Core out of this cycle's reach — no contract change. Having
    /// <c>ListAsync</c> hand back its failures would be the tidier design and it is a Core change;
    /// the inference above is honest without one, so the deviation was not spent. The cost is that
    /// this file holds a second copy of one rule, the filename-to-id derivation
    /// (<c>ThemeStore.cs:102</c>). <c>ThemeStoreReportTests</c> pays that cost off by running a
    /// REAL <see cref="ThemeStore"/> over a real folder and feeding its real output through here,
    /// so the copy cannot drift from the original without a red build.
    /// </para>
    /// <para>
    /// A file that is both malformed AND named after a built-in reports as the collision, since
    /// that is the reason it would still not appear once the JSON was fixed.
    /// </para>
    /// </summary>
    /// <param name="loaded">Exactly what <see cref="IThemeStore.ListAsync"/> returned.</param>
    /// <param name="fileNames">The <c>*.json</c> file names in the user themes folder, no paths.</param>
    internal static Line ForFolder(IEnumerable<Theme>? loaded, IEnumerable<string>? fileNames)
    {
        if (fileNames is null) return Silent;

        var byId = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in loaded ?? [])
        {
            byId[theme.Id] = theme.IsBuiltIn;
        }

        var unreadable = new List<string>();
        var shadowed = new List<string>();

        foreach (var fileName in fileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName)) continue;

            var id = Path.GetFileNameWithoutExtension(fileName);
            if (byId.TryGetValue(id, out var isBuiltIn))
            {
                if (isBuiltIn) shadowed.Add(fileName);
                continue;
            }

            unreadable.Add(fileName);
        }

        if (unreadable.Count == 0 && shadowed.Count == 0) return Silent;

        var parts = new List<string>(2);
        if (unreadable.Count > 0) parts.Add(UnreadableSentence(unreadable));
        if (shadowed.Count > 0) parts.Add(ShadowedSentence(shadowed));

        return Warn(string.Join(" ", parts));
    }

    /// <summary>
    /// Clan-facing register per <c>CLAUDE.md</c>: second person, terminal periods, no jargon. It
    /// names the file first because the file is the only part the reader can act on, and it says
    /// "reopen Settings" rather than "restart" because a restart is not what the code needs — see
    /// the themes-folder tooltip.
    /// <para>
    /// "A typo or a missing line" rather than the mechanism, and it covers both real causes:
    /// <c>ThemeStore</c> drops a file for malformed JSON (a typo) or for a required field that
    /// isn't there (a missing line in a pasted theme). "Malformed JSON" and "missing required
    /// property" are the true words and they are the wrong first read for somebody who pasted a
    /// theme out of a chat window.
    /// </para>
    /// </summary>
    private static string UnreadableSentence(List<string> files) =>
        files.Count == 1
            ? $"RoRoRo couldn't read {files[0]}, so it isn't in the list. Check it for a typo or a "
              + "missing line, then close and reopen Settings."
            : $"RoRoRo couldn't read {Count(files.Count)} files in your themes folder, so they "
              + $"aren't in the list: {Names(files)}. Check each one for a typo or a missing line, "
              + "then close and reopen Settings.";

    /// <summary>
    /// The other reason a file does not appear, and it is not a broken file — so it must not be
    /// reported as one. A built-in wins its own id (<c>ThemeStore.cs:73-76</c>), and the way out is
    /// a rename, which is the only advice here that would actually work.
    /// </summary>
    private static string ShadowedSentence(List<string> files) =>
        files.Count == 1
            ? $"{files[0]} has the same name as a built-in theme, so RoRoRo kept the built-in. "
              + "Rename the file to use yours."
            : $"{Count(files.Count)} files have the same names as built-in themes, so RoRoRo kept "
              + $"the built-ins: {Names(files)}. Rename them to use yours.";

    private static string Names(List<string> files) =>
        files.Count <= MaxNamed
            ? string.Join(", ", files)
            : string.Join(", ", files.Take(MaxNamed)) + $", and {Count(files.Count - MaxNamed)} more";

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The glyph is part of the TEXT, not only the brush. A warning that survives colour being
    /// taken away is the rule the flatline theme made non-negotiable, and it is why every other
    /// warning surface in this app carries the same prefix.
    /// </summary>
    private static Line Warn(string text) => new(true, WarnGlyph + " " + text);

    /// <summary>
    /// An exception message is not guaranteed to end in punctuation, and this one lands mid-sentence
    /// with a clause after it. Without this, "…is denied You'll be back…" ships.
    /// </summary>
    private static string Terminated(string text) =>
        text.Length > 0 && (text[^1] == '.' || text[^1] == '!' || text[^1] == '?')
            ? text
            : text + ".";
}
