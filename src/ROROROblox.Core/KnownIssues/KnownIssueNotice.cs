namespace ROROROblox.Core.KnownIssues;

/// <summary>The entries the main-window notice is about, newest first. Data only; the App words it.</summary>
public sealed record KnownIssueNoticeSelection(IReadOnlyList<KnownIssue> Issues)
{
    public static readonly KnownIssueNoticeSelection None = new([]);

    public bool IsEmpty => Issues.Count == 0;

    public IReadOnlyList<string> Ids => Issues.Select(i => i.Id).ToList();
}

public static class KnownIssueNotice
{
    /// <summary>Serious (<c>notify</c>), applicable to this PC, and not dismissed — newest first, id as the tiebreak.</summary>
    public static KnownIssueNoticeSelection Select(
        IReadOnlyList<KnownIssue> issues, Version? running, IReadOnlySet<string> dismissed) =>
        new(issues
            .Where(i => i.Notify && KnownIssueApplicability.Applies(i, running) && !dismissed.Contains(i.Id))
            .OrderByDescending(i => i.PostedAt)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList());

    /// <summary>The menu count: every entry that applies to this PC, serious or not.</summary>
    public static int CountApplicable(IReadOnlyList<KnownIssue> issues, Version? running) =>
        issues.Count(i => KnownIssueApplicability.Applies(i, running));

    /// <summary>Page order: serious first, then newest, then id so the order never shuffles between refreshes.</summary>
    public static IReadOnlyList<KnownIssue> PageOrder(IReadOnlyList<KnownIssue> issues) =>
        issues
            .OrderByDescending(i => i.Notify)
            .ThenByDescending(i => i.PostedAt)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
}
