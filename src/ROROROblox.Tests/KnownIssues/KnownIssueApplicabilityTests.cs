using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

public class KnownIssueApplicabilityTests
{
    private static readonly Version V739 = Version.Parse("0.739.0.7390687");
    private static readonly Version V740 = Version.Parse("0.740.0.7400927");

    [Fact]
    public void AnIssueWithNoVersionsAlwaysApplies()
    {
        Assert.Equal(KnownIssueVersionStatus.NoVersions, KnownIssueApplicability.StatusFor(Issue("a"), V740));
        Assert.True(KnownIssueApplicability.Applies(Issue("a"), null));
    }

    [Fact]
    public void BelowFixedInIsAffected_AtOrAboveIsFixed()
    {
        var issue = Issue("a", fixedIn: new Version(0, 740));

        Assert.Equal(KnownIssueVersionStatus.Affected, KnownIssueApplicability.StatusFor(issue, V739));
        Assert.Equal(KnownIssueVersionStatus.Fixed, KnownIssueApplicability.StatusFor(issue, V740));
        Assert.False(KnownIssueApplicability.Applies(issue, V740));
    }

    [Fact]
    public void BelowFromIsNotYetAffected_AtFromIsAffected()
    {
        var issue = Issue("a", from: new Version(0, 740));

        Assert.Equal(KnownIssueVersionStatus.NotYetAffected, KnownIssueApplicability.StatusFor(issue, V739));
        Assert.Equal(KnownIssueVersionStatus.Affected, KnownIssueApplicability.StatusFor(issue, V740));
    }

    [Fact]
    public void BothBoundsMakeAWindow()
    {
        var issue = Issue("a", from: new Version(0, 739), fixedIn: new Version(0, 740));

        Assert.True(KnownIssueApplicability.Applies(issue, V739));
        Assert.False(KnownIssueApplicability.Applies(issue, V740));
        Assert.False(KnownIssueApplicability.Applies(issue, Version.Parse("0.738.0.1")));
    }

    /// <summary>When unsure, tell: an unreadable version counts as affected, and says so on the page.</summary>
    [Fact]
    public void AnUnreadableVersionCountsAsAffected()
    {
        var issue = Issue("a", fixedIn: new Version(0, 740));

        Assert.Equal(KnownIssueVersionStatus.UnknownInstalled, KnownIssueApplicability.StatusFor(issue, null));
        Assert.True(KnownIssueApplicability.Applies(issue, null));
    }
}
