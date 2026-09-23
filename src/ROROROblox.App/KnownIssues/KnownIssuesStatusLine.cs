using System.Globalization;
using ROROROblox.App.Localization;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.App.KnownIssues;

/// <summary>
/// Decides the page's "Last checked" line and whether the page may show "No known Roblox issues
/// right now." (<c>ShowAllClear</c>) — spec §3. Split out as one pure function, tested on its own,
/// because a failed download used to still stamp <c>CheckedAt</c> with no record of the failure: a
/// fresh install behind a network that blocks GitHub read "No known Roblox issues right now. Last
/// checked at 12:47 PM.", a false all-clear the page had never actually verified.
/// </summary>
internal static class KnownIssuesStatusLine
{
    /// <summary>(Text, ShowAllClear): ShowAllClear says whether the page may show "No known Roblox issues right now."</summary>
    public static (string Text, bool ShowAllClear) For(
        KnownIssuesSource source,
        int issueCount,
        DateTimeOffset? checkedAt,
        KnownIssuesRefreshKind? lastOutcome,
        DateTimeOffset now,
        CultureInfo culture)
    {
        if (checkedAt is null)
        {
            // A saved copy that is itself empty is a real, verified empty list; with no source at
            // all, nothing is known yet.
            return (Loc.Get("KnownIssuesPage_Checking"), issueCount == 0 && source != KnownIssuesSource.None);
        }

        if (lastOutcome == KnownIssuesRefreshKind.Updated)
        {
            return (Loc.Format("KnownIssuesPage_LastChecked", When(checkedAt.Value, now, culture)), true);
        }

        // The last attempt failed.
        return source == KnownIssuesSource.None
            ? (Loc.Get("KnownIssuesPage_NeverReached"), false)
            : (Loc.Format("KnownIssuesPage_CheckFailed", When(checkedAt.Value, now, culture)), true);
    }

    /// <summary>Local time; time-only if the stamp falls on today's local calendar date, else short date + time.</summary>
    private static string When(DateTimeOffset checkedAt, DateTimeOffset now, CultureInfo culture)
    {
        var local = checkedAt.ToLocalTime();
        var isToday = local.Date == now.ToLocalTime().Date;
        return isToday ? local.ToString("t", culture) : local.ToString("g", culture);
    }
}
