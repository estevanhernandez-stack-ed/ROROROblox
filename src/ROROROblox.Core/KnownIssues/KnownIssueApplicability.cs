namespace ROROROblox.Core.KnownIssues;

public enum KnownIssueVersionStatus
{
    /// <summary>The entry names no versions; it stands until the owner removes it.</summary>
    NoVersions,
    Affected,
    /// <summary>The running version is older than <see cref="KnownIssueVersions.From"/>.</summary>
    NotYetAffected,
    Fixed,
    /// <summary>The entry names versions but the running version could not be read.</summary>
    UnknownInstalled,
}

/// <summary>
/// Does an issue apply to this PC? Decides the notice and the count only — the page always lists
/// every entry, because the client actually running can be older than the one installed (the frozen
/// window on 2026-09-22 was 0.739, opened from Chrome, with 0.740 installed).
/// </summary>
public static class KnownIssueApplicability
{
    public static KnownIssueVersionStatus StatusFor(KnownIssue issue, Version? running)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var range = issue.RobloxVersions;
        if (range is null)
        {
            return KnownIssueVersionStatus.NoVersions;
        }

        if (running is null)
        {
            return KnownIssueVersionStatus.UnknownInstalled;
        }

        if (range.From is { } from && running < from)
        {
            return KnownIssueVersionStatus.NotYetAffected;
        }

        if (range.FixedIn is { } fixedIn && running >= fixedIn)
        {
            return KnownIssueVersionStatus.Fixed;
        }

        return KnownIssueVersionStatus.Affected;
    }

    /// <summary>When unsure, tell: an unreadable version applies.</summary>
    public static bool Applies(KnownIssue issue, Version? running) => StatusFor(issue, running) is
        KnownIssueVersionStatus.NoVersions or KnownIssueVersionStatus.Affected or KnownIssueVersionStatus.UnknownInstalled;
}
