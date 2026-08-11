using System.Globalization;
using ROROROblox.App.ViewModels;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Preferences;

/// <summary>
/// How many accounts are muted, and the two clears that undo it.
/// <para>
/// WHY THIS EXISTS. F-024. Muting is per-account and is done by right-clicking a row; the Alerts
/// page owns every other alert decision and owned nothing about this one. Somebody who muted an
/// account three weeks ago had no way to find out from the app that they had, and no way back
/// except right-clicking every row in turn.
/// </para>
/// <para>
/// <b>NO NEW PERSISTENCE, and the count reads the ROWS.</b> The muted set already persists as
/// <c>DiscordConfig.MutedAccountIds</c> and is painted onto
/// <see cref="AccountSummary.AlertsMuted"/> at <c>App.xaml.cs:1376</c>. Counting the persisted ids
/// instead would count accounts that no longer exist — an id stays in that list when its account is
/// removed — and report "3 accounts are muted" to somebody who has two. The rows are what a user
/// can see and what an unmute changes, so the rows are what gets counted.
/// </para>
/// <para>
/// SPLIT IN TWO ON PURPOSE. <see cref="UnmuteRows"/> is the live half and <see cref="WithoutMutes"/>
/// is the persisted half, because the caller has to do them in that order and undo only the first
/// if the save throws. Neither is reachable from the other, so neither can quietly become the
/// second source of truth for the other's state.
/// </para>
/// <para>
/// Pure and window-free so the count, the clears and the zero case are testable — the test suite
/// constructs no <c>Window</c>, by design, and a rule that only exists inside a click handler is a
/// rule nothing watches.
/// </para>
/// </summary>
internal static class MutedAccountsSummary
{
    /// <summary>
    /// What the Alerts section should say and whether it should be on screen at all.
    /// <paramref name="Any"/> is what drives visibility: <c>prd.md &gt; Story 1.3</c> requires zero
    /// muted to read as a clean state rather than a stray "0" or an empty row, so it is a property
    /// of this record and not a comparison written at the call site.
    /// </summary>
    internal readonly record struct Summary(int Count, bool Any, string Text);

    /// <summary>
    /// Counts the muted rows and writes the sentence for them. Zero returns
    /// <see cref="string.Empty"/> as well as <c>Any == false</c> — a collapsed line holding stale
    /// text is one theme change away from being visible again saying the wrong number.
    /// </summary>
    internal static Summary Describe(IEnumerable<AccountSummary>? rows)
    {
        var count = rows?.Count(row => row.AlertsMuted) ?? 0;
        return new Summary(count, count > 0, count == 0 ? string.Empty : Sentence(count));
    }

    /// <summary>
    /// Clan-facing register per <c>CLAUDE.md</c>: plain words, terminal periods, no jargon. It says
    /// what muting DOES rather than restating the label, because the reader here is somebody who
    /// muted a row weeks ago and is being reminded, not taught.
    /// </summary>
    private static string Sentence(int count) =>
        count == 1
            ? "1 account is muted. Nothing it does raises an alert, whatever the settings above say."
            : $"{count.ToString(CultureInfo.InvariantCulture)} accounts are muted. Nothing they do "
              + "raises an alert, whatever the settings above say.";

    /// <summary>
    /// Turns the live mute off on every row that carried it, and hands back exactly those rows.
    /// <para>
    /// The return value is the undo. If the save that follows fails, the caller puts the mute back
    /// on precisely the rows this turned off — no id set, no second copy of
    /// <c>App.xaml.cs:1376</c>'s hydration rule, and no chance of reviving a mute that was never on.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<AccountSummary> UnmuteRows(IEnumerable<AccountSummary>? rows)
    {
        if (rows is null) return [];

        var cleared = rows.Where(row => row.AlertsMuted).ToList();
        foreach (var row in cleared)
        {
            row.AlertsMuted = false;
        }

        return cleared;
    }

    /// <summary>
    /// The persisted half: the same config with the muted list emptied and every other field
    /// carried through untouched. Emptying rather than removing the rows' ids one by one is
    /// deliberate — "unmute all" means all, including an id left behind by an account that was
    /// removed while muted, which nothing else in the app will ever clean up.
    /// <para>
    /// A <c>with</c> expression rather than a hand-built record, for the reason
    /// <c>AccountMuteTests.MutingOneRow_LeavesEveryOtherSettingAlone</c> pins one level up: this
    /// record also holds the webhook URLs and the presence toggle, and rebuilding it by hand is how
    /// somebody's credentials get silently wiped by a mute.
    /// </para>
    /// </summary>
    internal static DiscordConfig WithoutMutes(DiscordConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config with { MutedAccountIds = [] };
    }
}
