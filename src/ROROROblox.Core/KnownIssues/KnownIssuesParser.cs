using System.Globalization;
using System.Text.Json;

namespace ROROROblox.Core.KnownIssues;

public enum KnownIssuesProblemKind
{
    NotJson,
    UnsupportedSchemaVersion,
    MissingField,
    DuplicateId,
    BadDate,
    NonHttpsLink,
    BadVersionFormat,
    EmptyVersionRange,
}

/// <summary>One reason a document was refused: a kind plus data, never prose (Core string boundary).</summary>
public sealed record KnownIssuesProblem(KnownIssuesProblemKind Kind, string? IssueId, string? Field);

public sealed record KnownIssuesParseResult(KnownIssuesDocument? Document, IReadOnlyList<KnownIssuesProblem> Problems)
{
    public bool IsValid => Document is not null && Problems.Count == 0;
}

/// <summary>
/// The single validator for <c>known-issues.json</c>. <c>tools/CompatSigner --validate-known-issues</c>
/// runs it before CI signs the file, and <c>KnownIssuesFeed</c> runs it after the signature verifies,
/// so the workflow can never sign a file every client would refuse. A document is all or nothing: any
/// problem refuses the whole file, and the app keeps what it had.
/// <para>
/// Deliberately NOT refused: an unknown <c>rororoHelps.feature</c> key (a newer feed must load on an
/// older app) and unknown fields (a later format may add some).
/// </para>
/// </summary>
public static class KnownIssuesParser
{
    public const int SupportedSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // The file is edited by hand; a trailing comma or a comment is not worth a failed publish.
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static KnownIssuesParseResult Parse(ReadOnlySpan<byte> utf8Json)
    {
        DocumentDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<DocumentDto>(utf8Json, Options);
        }
        catch (JsonException)
        {
            return Refused([new KnownIssuesProblem(KnownIssuesProblemKind.NotJson, null, null)]);
        }

        if (dto is null)
        {
            return Refused([new KnownIssuesProblem(KnownIssuesProblemKind.NotJson, null, null)]);
        }

        var problems = new List<KnownIssuesProblem>();
        if (dto.SchemaVersion is null)
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, null, "schemaVersion"));
        }
        else if (dto.SchemaVersion != SupportedSchemaVersion)
        {
            problems.Add(new(KnownIssuesProblemKind.UnsupportedSchemaVersion, null, "schemaVersion"));
        }

        if (dto.Issues is null)
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, null, "issues"));
            return Refused(problems);
        }

        var issues = new List<KnownIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in dto.Issues)
        {
            if (ReadIssue(raw, problems, seen) is { } issue)
            {
                issues.Add(issue);
            }
        }

        return problems.Count == 0
            ? new KnownIssuesParseResult(new KnownIssuesDocument(SupportedSchemaVersion, issues), [])
            : Refused(problems);
    }

    private static KnownIssuesParseResult Refused(IReadOnlyList<KnownIssuesProblem> problems) => new(null, problems);

    private static KnownIssue? ReadIssue(IssueDto? raw, List<KnownIssuesProblem> problems, HashSet<string> seen)
    {
        if (raw is null)
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, null, "issues[]"));
            return null;
        }

        var id = raw.Id?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, null, "id"));
            return null;
        }

        var before = problems.Count;
        if (!seen.Add(id))
        {
            problems.Add(new(KnownIssuesProblemKind.DuplicateId, id, "id"));
        }

        DateOnly postedAt = default;
        if (string.IsNullOrWhiteSpace(raw.PostedAt))
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, id, "postedAt"));
        }
        else if (!DateOnly.TryParseExact(raw.PostedAt.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out postedAt))
        {
            problems.Add(new(KnownIssuesProblemKind.BadDate, id, "postedAt"));
        }

        if (raw.Notify is null)
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, id, "notify"));
        }

        RequireText(raw.Title, id, "title", problems);
        RequireText(raw.Symptom, id, "symptom", problems);
        RequireText(raw.Workaround, id, "workaround", problems);

        KnownIssueHelp? help = null;
        if (raw.RororoHelps is { } h)
        {
            RequireText(h.Feature, id, "rororoHelps.feature", problems);
            RequireText(h.Text, id, "rororoHelps.text", problems);
            if (!string.IsNullOrWhiteSpace(h.Feature) && !string.IsNullOrWhiteSpace(h.Text))
            {
                help = new KnownIssueHelp(h.Feature.Trim(), h.Text.Trim());
            }
        }

        var links = new List<KnownIssueLink>();
        foreach (var link in raw.Links ?? [])
        {
            if (link is null || string.IsNullOrWhiteSpace(link.Label))
            {
                problems.Add(new(KnownIssuesProblemKind.MissingField, id, "links.label"));
                continue;
            }

            var url = link.Url?.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                problems.Add(new(KnownIssuesProblemKind.NonHttpsLink, id, "links.url"));
                continue;
            }

            links.Add(new KnownIssueLink(link.Label.Trim(), url!));
        }

        KnownIssueVersions? versions = null;
        if (raw.RobloxVersions is { } v)
        {
            if (v.From is null && v.FixedIn is null)
            {
                problems.Add(new(KnownIssuesProblemKind.EmptyVersionRange, id, "robloxVersions"));
            }
            else
            {
                Version? from = null;
                Version? fixedIn = null;
                if (v.From is not null && !RobloxVersion.TryParseFeed(v.From, out from))
                {
                    problems.Add(new(KnownIssuesProblemKind.BadVersionFormat, id, "robloxVersions.from"));
                }

                if (v.FixedIn is not null && !RobloxVersion.TryParseFeed(v.FixedIn, out fixedIn))
                {
                    problems.Add(new(KnownIssuesProblemKind.BadVersionFormat, id, "robloxVersions.fixedIn"));
                }

                versions = new KnownIssueVersions(from, fixedIn);
            }
        }

        if (problems.Count != before)
        {
            return null;
        }

        return new KnownIssue(
            id,
            postedAt,
            raw.Notify!.Value,
            raw.Title!.Trim(),
            raw.Symptom!.Trim(),
            raw.Workaround!.Trim(),
            help,
            links,
            versions);
    }

    private static void RequireText(string? value, string id, string field, List<KnownIssuesProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add(new(KnownIssuesProblemKind.MissingField, id, field));
        }
    }

    internal sealed class DocumentDto
    {
        public int? SchemaVersion { get; set; }
        public List<IssueDto?>? Issues { get; set; }
    }

    internal sealed class IssueDto
    {
        public string? Id { get; set; }
        public string? PostedAt { get; set; }
        public bool? Notify { get; set; }
        public string? Title { get; set; }
        public string? Symptom { get; set; }
        public string? Workaround { get; set; }
        public HelpDto? RororoHelps { get; set; }
        public List<LinkDto?>? Links { get; set; }
        public VersionsDto? RobloxVersions { get; set; }
    }

    internal sealed class HelpDto
    {
        public string? Feature { get; set; }
        public string? Text { get; set; }
    }

    internal sealed class LinkDto
    {
        public string? Label { get; set; }
        public string? Url { get; set; }
    }

    internal sealed class VersionsDto
    {
        public string? From { get; set; }
        public string? FixedIn { get; set; }
    }
}
