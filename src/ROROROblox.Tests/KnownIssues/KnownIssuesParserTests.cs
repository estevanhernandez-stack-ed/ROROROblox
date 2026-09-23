using System.Text;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// The one validator. <c>tools/CompatSigner</c> runs it before signing and the app runs it after
/// verifying, so "CI never signs a file the app would reject" is true by construction. Every refusal
/// in the spec's §1 has a case here.
/// </summary>
public class KnownIssuesParserTests
{
    private static byte[] Bytes(string json) => Encoding.UTF8.GetBytes(json);

    private const string ValidDocument = """
        {
          "schemaVersion": 1,
          "issues": [
            {
              "id": "window-freeze",
              "postedAt": "2026-09-23",
              "notify": true,
              "title": "The Roblox window freezes when you drag it",
              "symptom": "Dragging stops responding.",
              "workaround": "Press the Windows key, then Escape.",
              "rororoHelps": { "feature": "fps-caps", "text": "Cap below your refresh rate." },
              "links": [ { "label": "DevForum", "url": "https://devforum.roblox.com/t/4032374" } ],
              "robloxVersions": { "from": "0.739", "fixedIn": "0.740" }
            }
          ]
        }
        """;

    /// <summary>A one-issue document whose issue object is exactly <paramref name="issueBody"/>.</summary>
    private static string OneIssue(string issueBody) =>
        "{ \"schemaVersion\": 1, \"issues\": [ { " + issueBody + " } ] }";

    private const string Required =
        "\"id\": \"a\", \"postedAt\": \"2026-09-23\", \"notify\": false, " +
        "\"title\": \"t\", \"symptom\": \"s\", \"workaround\": \"w\"";

    [Fact]
    public void ParsesEveryField()
    {
        var result = KnownIssuesParser.Parse(Bytes(ValidDocument));

        Assert.True(result.IsValid, string.Join(", ", result.Problems));
        var issue = Assert.Single(result.Document!.Issues);
        Assert.Equal("window-freeze", issue.Id);
        Assert.Equal(new DateOnly(2026, 9, 23), issue.PostedAt);
        Assert.True(issue.Notify);
        Assert.Equal("The Roblox window freezes when you drag it", issue.Title);
        Assert.Equal("Dragging stops responding.", issue.Symptom);
        Assert.Equal("Press the Windows key, then Escape.", issue.Workaround);
        Assert.Equal(new KnownIssueHelp("fps-caps", "Cap below your refresh rate."), issue.RororoHelps);
        Assert.Equal(new KnownIssueLink("DevForum", "https://devforum.roblox.com/t/4032374"), Assert.Single(issue.Links));
        Assert.Equal(new Version(0, 739), issue.RobloxVersions!.From);
        Assert.Equal(new Version(0, 740), issue.RobloxVersions.FixedIn);
    }

    [Fact]
    public void AnEmptyIssueListIsValid()
    {
        var result = KnownIssuesParser.Parse(Bytes("{ \"schemaVersion\": 1, \"issues\": [] }"));

        Assert.True(result.IsValid);
        Assert.Empty(result.Document!.Issues);
    }

    [Fact]
    public void OptionalFieldsMayBeLeftOut()
    {
        var result = KnownIssuesParser.Parse(Bytes(OneIssue(Required)));

        Assert.True(result.IsValid, string.Join(", ", result.Problems));
        var issue = Assert.Single(result.Document!.Issues);
        Assert.Null(issue.RororoHelps);
        Assert.Empty(issue.Links);
        Assert.Null(issue.RobloxVersions);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        var json = "{ \"schemaVersion\": 1, \"futureThing\": 3, \"issues\": [ { " + Required + ", \"severity\": \"high\" } ] }";

        Assert.True(KnownIssuesParser.Parse(Bytes(json)).IsValid);
    }

    /// <summary>Review Focus 1: a newer feed names a feature this build does not know; it must still load.</summary>
    [Fact]
    public void AnUnknownFeatureKeyIsAccepted()
    {
        var json = OneIssue(Required + ", \"rororoHelps\": { \"feature\": \"teleport-helper\", \"text\": \"Soon.\" }");

        var result = KnownIssuesParser.Parse(Bytes(json));

        Assert.True(result.IsValid, string.Join(", ", result.Problems));
        Assert.Equal("teleport-helper", result.Document!.Issues[0].RororoHelps!.Feature);
    }

    [Fact]
    public void TrailingCommasAndCommentsAreAcceptedForHandEditing()
    {
        var json = "{ // hand edited\n \"schemaVersion\": 1, \"issues\": [ { " + Required + ", }, ], }";

        Assert.True(KnownIssuesParser.Parse(Bytes(json)).IsValid);
    }

    [Theory]
    [InlineData("not json at all", KnownIssuesProblemKind.NotJson, null)]
    [InlineData("[]", KnownIssuesProblemKind.NotJson, null)]
    [InlineData("null", KnownIssuesProblemKind.NotJson, null)]
    [InlineData("{ \"schemaVersion\": 2, \"issues\": [] }", KnownIssuesProblemKind.UnsupportedSchemaVersion, "schemaVersion")]
    [InlineData("{ \"issues\": [] }", KnownIssuesProblemKind.MissingField, "schemaVersion")]
    [InlineData("{ \"schemaVersion\": 1 }", KnownIssuesProblemKind.MissingField, "issues")]
    public void ADocumentLevelProblemIsRefused(string json, KnownIssuesProblemKind kind, string? field)
    {
        var result = KnownIssuesParser.Parse(Bytes(json));

        Assert.False(result.IsValid);
        Assert.Null(result.Document);
        Assert.Contains(result.Problems, p => p.Kind == kind && p.Field == field);
    }

    [Theory]
    [InlineData("\"id\": \"a\", \"postedAt\": \"2026-09-23\", \"notify\": true, \"symptom\": \"s\", \"workaround\": \"w\"", KnownIssuesProblemKind.MissingField, "title")]
    [InlineData("\"id\": \"a\", \"postedAt\": \"2026-09-23\", \"title\": \"t\", \"symptom\": \"s\", \"workaround\": \"w\"", KnownIssuesProblemKind.MissingField, "notify")]
    [InlineData("\"id\": \"a\", \"postedAt\": \"23/09/2026\", \"notify\": true, \"title\": \"t\", \"symptom\": \"s\", \"workaround\": \"w\"", KnownIssuesProblemKind.BadDate, "postedAt")]
    public void AnIssueMissingOrMisspellingARequiredFieldIsRefused(string issueBody, KnownIssuesProblemKind kind, string field)
    {
        var result = KnownIssuesParser.Parse(Bytes(OneIssue(issueBody)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == kind && p.IssueId == "a" && p.Field == field);
    }

    [Fact]
    public void AnIssueWithNoIdIsRefused()
    {
        var body = "\"postedAt\": \"2026-09-23\", \"notify\": true, \"title\": \"t\", \"symptom\": \"s\", \"workaround\": \"w\"";

        var result = KnownIssuesParser.Parse(Bytes(OneIssue(body)));

        Assert.Contains(result.Problems, p => p.Kind == KnownIssuesProblemKind.MissingField && p.Field == "id");
    }

    [Fact]
    public void ADuplicateIdIsRefused()
    {
        var json = "{ \"schemaVersion\": 1, \"issues\": [ { " + Required + " }, { " + Required + " } ] }";

        var result = KnownIssuesParser.Parse(Bytes(json));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == KnownIssuesProblemKind.DuplicateId && p.IssueId == "a");
    }

    [Theory]
    [InlineData("http://devforum.roblox.com/t/1")]
    [InlineData("javascript:alert(1)")]
    [InlineData("devforum.roblox.com/t/1")]
    [InlineData("file:///C:/Windows/notepad.exe")]
    public void ALinkThatIsNotHttpsIsRefused(string url)
    {
        var body = Required + ", \"links\": [ { \"label\": \"L\", \"url\": \"" + url + "\" } ]";

        var result = KnownIssuesParser.Parse(Bytes(OneIssue(body)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == KnownIssuesProblemKind.NonHttpsLink && p.Field == "links.url");
    }

    [Theory]
    [InlineData("{ \"fixedIn\": \"0.74\" }", KnownIssuesProblemKind.BadVersionFormat, "robloxVersions.fixedIn")]
    [InlineData("{ \"from\": \"zero\" }", KnownIssuesProblemKind.BadVersionFormat, "robloxVersions.from")]
    [InlineData("{ }", KnownIssuesProblemKind.EmptyVersionRange, "robloxVersions")]
    public void ABadVersionRangeIsRefused(string range, KnownIssuesProblemKind kind, string field)
    {
        var result = KnownIssuesParser.Parse(Bytes(OneIssue(Required + ", \"robloxVersions\": " + range)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == kind && p.Field == field);
    }

    [Fact]
    public void AHelpBlockWithoutItsSentenceIsRefused()
    {
        var body = Required + ", \"rororoHelps\": { \"feature\": \"fps-caps\" }";

        var result = KnownIssuesParser.Parse(Bytes(OneIssue(body)));

        Assert.Contains(result.Problems, p => p.Kind == KnownIssuesProblemKind.MissingField && p.Field == "rororoHelps.text");
    }
}
