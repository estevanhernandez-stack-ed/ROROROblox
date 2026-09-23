using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// The app reads only from the release marked Latest, so a release without the feed silences the
/// page the day it ships. Nothing like this existed for plugins-catalog.json, which is how it sat at
/// Ur Score 0.3.5 for months.
/// </summary>
public class KnownIssuesPublishingFenceTests
{
    private static string Root()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx above the test assembly.");
        return root!;
    }

    [Theory]
    [InlineData("release.yml")]
    [InlineData("compat.yml")]
    public void TheWorkflowValidatesTheFeed_ThenAttachesItWithItsSignature(string workflow)
    {
        var text = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", workflow));

        Assert.Contains("--validate-known-issues", text);
        Assert.Contains("known-issues.json known-issues.json.sig", text);
    }

    /// <summary>A bad edit fails CI here, before anyone runs a workflow against it.</summary>
    [Fact]
    public void TheCommittedFeedIsValid()
    {
        var result = KnownIssuesParser.Parse(File.ReadAllBytes(Path.Combine(Root(), "known-issues.json")));

        Assert.True(result.IsValid, string.Join(", ", result.Problems.Select(p => $"{p.Kind} {p.IssueId} {p.Field}")));
    }
}
