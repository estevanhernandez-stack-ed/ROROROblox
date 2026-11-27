using ROROROblox.App.Localization;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Feeds;

namespace ROROROblox.App.Notices;

/// <summary>Declaration order is the precedence: the first notice that is true is drawn, the rest wait behind "more".</summary>
internal enum RobloxNoticeKind { Contested, Outage, KnownIssue, Drift, Update }

/// <summary>Contested, outage and drift are warnings; a known issue and an update are plain statements (ruled A1, 2026-11-26).</summary>
public enum RobloxNoticeTone { Warning, Info }

/// <param name="ContestedText">Empty when the name is not contested.</param>
/// <param name="KnownIssueText">Empty when there is nothing to show or it was dismissed. Not blanked by a higher notice.</param>
/// <param name="DriftText">Always empty in the app until the drift banner is repaired; the order is defined and tested anyway.</param>
/// <param name="HoldBannerShowing"><c>MainViewModel.RobloxUpdating</c>: the hold banner already says the update is being installed.</param>
internal sealed record RobloxLineInputs(
    string ContestedText,
    RobloxStatusReading Status,
    string KnownIssueText,
    string DriftText,
    RobloxUpdateState Update,
    bool HoldBannerShowing,
    bool HasMultipleAccounts,
    bool Compact);

/// <param name="WithLink">The notice carries a link out: the status page for an outage, the release notes for a newer update.</param>
internal sealed record RobloxNotice(RobloxNoticeKind Kind, string Text, RobloxNoticeTone Tone, bool WithLink);

internal sealed record RobloxLineState(RobloxNotice? Top, IReadOnlyList<RobloxNotice> More)
{
    public static RobloxLineState Empty { get; } = new(null, []);
}

/// <summary>
/// Picks the one Roblox sentence the window draws. Pure apart from the string catalog: inputs in, state out.
/// Remote data chooses a sentence and never supplies one, so every word here comes from the catalog.
/// </summary>
internal static class RobloxNoticeResolver
{
    public static RobloxLineState Resolve(RobloxLineInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var notices = new List<RobloxNotice>(5);

        if (inputs.ContestedText.Length > 0)
        {
            notices.Add(new(RobloxNoticeKind.Contested, inputs.ContestedText, RobloxNoticeTone.Warning, WithLink: false));
        }

        if (inputs.Status.Worst is { } worst)
        {
            notices.Add(new(RobloxNoticeKind.Outage, StatusText(inputs.Status, worst), RobloxNoticeTone.Warning, WithLink: true));
        }

        if (inputs.KnownIssueText.Length > 0)
        {
            notices.Add(new(RobloxNoticeKind.KnownIssue, inputs.KnownIssueText, RobloxNoticeTone.Info, WithLink: false));
        }

        if (inputs.DriftText.Length > 0)
        {
            notices.Add(new(RobloxNoticeKind.Drift, inputs.DriftText, RobloxNoticeTone.Warning, WithLink: false));
        }

        if (inputs.Update.Kind == RobloxUpdateKind.Pending && !inputs.HoldBannerShowing)
        {
            // Compact has the least room, and the batch clause is the long half of the sentence.
            var key = inputs.HasMultipleAccounts && !inputs.Compact ? "RobloxLine_Update_Many" : "RobloxLine_Update_One";
            notices.Add(new(
                RobloxNoticeKind.Update,
                Loc.Get(key),
                RobloxNoticeTone.Info,
                WithLink: inputs.Update.Direction == RobloxUpdateDirection.Newer));
        }

        return notices.Count == 0
            ? RobloxLineState.Empty
            : new RobloxLineState(notices[0], notices.Skip(1).ToList());
    }

    private static string StatusText(RobloxStatusReading status, RobloxStatusRow worst)
    {
        var row = Loc.Get(RowKey(worst));
        var named = status.OtherRows > 0 ? WithOthers(row, status.OtherRows) : row;
        return Loc.Format(SentenceKey(status.Level), named);
    }

    /// <summary>"{0} and {1} more": the row, then the count, which also picks the plural form.</summary>
    private static string WithOthers(string row, int others)
    {
        var culture = TranslationSource.Instance.CurrentCulture;
        var template = Loc.Get($"RobloxLine_Status_AndMore_{Plurals.Category(culture, others)}");
        return string.Format(culture, template, row, others);
    }

    private static string SentenceKey(RobloxStatusLevel level) => level switch
    {
        RobloxStatusLevel.Outage => "RobloxLine_Status_Outage",
        RobloxStatusLevel.SecurityEvent => "RobloxLine_Status_Security",
        RobloxStatusLevel.PartialOutage => "RobloxLine_Status_Partial",
        RobloxStatusLevel.Degraded => "RobloxLine_Status_Degraded",
        RobloxStatusLevel.Maintenance => "RobloxLine_Status_Maintenance",
        _ => "RobloxLine_Status_Problem",
    };

    private static string RowKey(RobloxStatusRow row) => row switch
    {
        RobloxStatusRow.Website => "RobloxLine_Row_Website",
        RobloxStatusRow.Games => "RobloxLine_Row_Games",
        RobloxStatusRow.Avatar => "RobloxLine_Row_Avatar",
        RobloxStatusRow.GameJoin => "RobloxLine_Row_GameJoin",
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, null),
    };
}
