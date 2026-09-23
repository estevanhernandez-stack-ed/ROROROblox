using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

internal static class KnownIssueBuilder
{
    public static KnownIssue Issue(
        string id,
        bool notify = true,
        string posted = "2026-09-23",
        Version? from = null,
        Version? fixedIn = null,
        KnownIssueHelp? helps = null,
        params KnownIssueLink[] links) => new(
            id,
            DateOnly.Parse(posted, System.Globalization.CultureInfo.InvariantCulture),
            notify,
            $"Title of {id}",
            $"Symptom of {id}",
            $"Workaround for {id}",
            helps,
            links,
            from is null && fixedIn is null ? null : new KnownIssueVersions(from, fixedIn));
}
