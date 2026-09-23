using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

public class KnownIssueNoticeTests
{
    private static readonly Version V740 = Version.Parse("0.740.0.7400927");
    private static readonly IReadOnlySet<string> NoneDismissed = new HashSet<string>();

    [Fact]
    public void OnlyApplicableUndismissedNotifyEntriesAreSelected_NewestFirst()
    {
        var issues = new[]
        {
            Issue("old", posted: "2026-09-01"),
            Issue("quiet", notify: false),
            Issue("fixed", fixedIn: new Version(0, 740)),
            Issue("new", posted: "2026-09-22"),
            Issue("closed"),
        };

        // "quiet" is not serious, "fixed" is fixed in 0.740, "closed" was dismissed.
        var selection = KnownIssueNotice.Select(issues, V740, new HashSet<string> { "closed" });

        Assert.Equal(["new", "old"], selection.Ids);
    }

    [Fact]
    public void NothingApplicableIsAnEmptySelection()
    {
        var selection = KnownIssueNotice.Select([Issue("quiet", notify: false)], V740, NoneDismissed);

        Assert.True(selection.IsEmpty);
        Assert.Empty(selection.Ids);
    }

    [Fact]
    public void TheCountIncludesQuietEntriesButNotFixedOnes()
    {
        var issues = new[] { Issue("loud"), Issue("quiet", notify: false), Issue("fixed", fixedIn: new Version(0, 740)) };

        Assert.Equal(2, KnownIssueNotice.CountApplicable(issues, V740));
    }

    [Fact]
    public void ThePageListsEveryEntry_NotifyFirst_ThenNewest_ThenById()
    {
        var issues = new[]
        {
            Issue("b-quiet", notify: false, posted: "2026-09-30"),
            Issue("loud-old", posted: "2026-09-01"),
            Issue("a-quiet", notify: false, posted: "2026-09-30"),
            Issue("loud-new", posted: "2026-09-20"),
        };

        var order = KnownIssueNotice.PageOrder(issues).Select(i => i.Id).ToArray();

        Assert.Equal(["loud-new", "loud-old", "a-quiet", "b-quiet"], order);
    }
}
