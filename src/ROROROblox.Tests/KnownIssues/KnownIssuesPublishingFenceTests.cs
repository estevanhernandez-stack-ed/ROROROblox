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

    /// <summary>
    /// The literal step invocation that actually validates known-issues.json at runtime -- as
    /// opposed to the bare flag name <c>--validate-known-issues</c>, which also appears in this
    /// file's own header comment (explaining the flag) well above every step. Anchoring on the
    /// bare flag would find that comment first regardless of step order and prove nothing; this is
    /// specific to the one line CompatSigner is actually invoked with it.
    /// </summary>
    private const string ValidateKnownIssuesInvocation =
        "dotnet run --project tools/CompatSigner --configuration Release -- --validate-known-issues $issuesPath";

    /// <summary>
    /// Final-fix wave, commit C: compat.yml used to validate known-issues.json BEFORE
    /// roblox-compat.json was signed or uploaded, so a malformed known-issues.json ended the job
    /// before the mutex-name file ever shipped -- exactly what keeping the two files separate was
    /// meant to prevent (design doc, "Two transports were rejected"). Failed against the
    /// pre-restructure compat.yml (index of the known-issues validate call came before the
    /// roblox-compat.json upload call); see the RED evidence in the final-fix report.
    /// </summary>
    [Fact]
    public void CompatWorkflow_UploadsRobloxCompatJson_BeforeItEverTouchesKnownIssues()
    {
        var text = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", "compat.yml"));

        var compatUploadIndex = text.IndexOf("roblox-compat.json roblox-compat.json.sig", StringComparison.Ordinal);
        var validateKnownIssuesIndex = text.IndexOf(ValidateKnownIssuesInvocation, StringComparison.Ordinal);

        Assert.True(compatUploadIndex >= 0, "compat.yml does not upload roblox-compat.json with its signature.");
        Assert.True(validateKnownIssuesIndex >= 0, "compat.yml does not invoke the known-issues validator.");
        Assert.True(
            compatUploadIndex < validateKnownIssuesIndex,
            "roblox-compat.json must be uploaded before compat.yml does anything with known-issues.json, " +
            "so a malformed known-issues.json can never block the mutex-name push.");
    }

    /// <summary>
    /// In both workflows, the app's own validator must have passed on known-issues.json before
    /// CompatSigner is ever asked to sign it -- signing an invalid file would mean the workflow
    /// shipped something every client's own parser would then refuse.
    /// </summary>
    [Theory]
    [InlineData("release.yml")]
    [InlineData("compat.yml")]
    public void BothWorkflows_ValidateKnownIssuesJson_BeforeSigningIt(string workflow)
    {
        var text = File.ReadAllText(Path.Combine(Root(), ".github", "workflows", workflow));

        var validateIndex = text.IndexOf(ValidateKnownIssuesInvocation, StringComparison.Ordinal);
        var signIndex = text.IndexOf(
            "dotnet run --project tools/CompatSigner --configuration Release -- $issuesPath", StringComparison.Ordinal);

        Assert.True(validateIndex >= 0, $"{workflow} does not invoke the known-issues validator.");
        Assert.True(signIndex >= 0, $"{workflow} does not sign known-issues.json.");
        Assert.True(validateIndex < signIndex, $"{workflow} must validate known-issues.json before signing it.");
    }
}
