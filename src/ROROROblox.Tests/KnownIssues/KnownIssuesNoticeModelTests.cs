using ROROROblox.App.KnownIssues;
using ROROROblox.App.Localization;
using ROROROblox.Core;
using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

public sealed class KnownIssuesNoticeModelTests : IDisposable
{
    private readonly string _dismissedPath = Path.Combine(Path.GetTempPath(), "rororo-notice-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_dismissedPath)) File.Delete(_dismissedPath);
    }

    internal sealed class InlineDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
    }

    private KnownIssuesNoticeModel Model(Version? running = null) =>
        new(new KnownIssuesDismissals(_dismissedPath), () => running, new InlineDispatcher());

    private static KnownIssuesSnapshot Snapshot(params KnownIssue[] issues) =>
        new(issues, KnownIssuesSource.Release, DateTimeOffset.UnixEpoch);

    [Fact]
    public void OneApplicableSeriousEntryIsNamedInTheNotice()
    {
        var model = Model();

        model.Apply(Snapshot(Issue("a")));

        Assert.True(model.HasNotice);
        Assert.Equal(Loc.Format("MainWindow_KnownIssueNoticeOne", "Title of a"), model.NoticeText);
    }

    [Fact]
    public void SeveralAreCounted()
    {
        var model = Model();

        model.Apply(Snapshot(Issue("a"), Issue("b")));

        Assert.Equal(Loc.Format("MainWindow_KnownIssueNoticeMany", 2), model.NoticeText);
    }

    [Fact]
    public void QuietAndFixedEntriesRaiseNoNotice_ButStayOnThePage()
    {
        var model = Model(Version.Parse("0.740.0.7400927"));

        model.Apply(Snapshot(Issue("quiet", notify: false), Issue("fixed", fixedIn: new Version(0, 740))));

        Assert.False(model.HasNotice);
        Assert.Equal(string.Empty, model.NoticeText);
        Assert.Equal(2, model.PageIssues.Count);
        Assert.Equal(1, model.ApplicableCount);
    }

    [Fact]
    public void TheContestedWarningSuppressesTheNotice_AndItComesBack()
    {
        var model = Model();
        model.Apply(Snapshot(Issue("a")));

        model.SetSuppressed(true);
        Assert.Equal(string.Empty, model.NoticeText);

        model.SetSuppressed(false);
        Assert.NotEqual(string.Empty, model.NoticeText);
    }

    [Fact]
    public void ClosingTheNoticeKeepsItClosed_UntilANewEntryArrives()
    {
        var model = Model();
        model.Apply(Snapshot(Issue("a")));

        model.Dismiss();
        Assert.False(model.HasNotice);

        var afterRestart = Model();
        afterRestart.Apply(Snapshot(Issue("a")));
        Assert.False(afterRestart.HasNotice);

        afterRestart.Apply(Snapshot(Issue("a"), Issue("b")));
        Assert.Equal(Loc.Format("MainWindow_KnownIssueNoticeOne", "Title of b"), afterRestart.NoticeText);
    }

    [Fact]
    public void TheMenuShowsACountOnlyWhenSomethingApplies()
    {
        var model = Model();
        Assert.Equal(Loc.Get("MainWindow_KnownRobloxIssues"), model.MenuHeader);

        model.Apply(Snapshot(Issue("a"), Issue("b", notify: false)));
        Assert.Equal(Loc.Format("MainWindow_KnownRobloxIssuesCount", 2), model.MenuHeader);
    }

    [Fact]
    public void EveryApplyRaisesChangedForThePage()
    {
        var model = Model();
        var raised = 0;
        model.Changed += (_, _) => raised++;

        model.Apply(Snapshot(Issue("a")));

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Final-fix wave, commit B: the page's status line (<c>KnownIssuesStatusLine</c>) needs both
    /// of these off the applied snapshot to tell a verified empty list from a check that failed.
    /// </summary>
    [Fact]
    public void ApplyCarriesSourceAndLastOutcomeFromTheSnapshot()
    {
        var model = Model();

        model.Apply(new KnownIssuesSnapshot([], KnownIssuesSource.SavedCopy, DateTimeOffset.UnixEpoch, KnownIssuesRefreshKind.NetworkFailed));

        Assert.Equal(KnownIssuesSource.SavedCopy, model.Source);
        Assert.Equal(KnownIssuesRefreshKind.NetworkFailed, model.LastOutcome);

        model.Apply(new KnownIssuesSnapshot([], KnownIssuesSource.Release, DateTimeOffset.UnixEpoch, KnownIssuesRefreshKind.Updated));

        Assert.Equal(KnownIssuesSource.Release, model.Source);
        Assert.Equal(KnownIssuesRefreshKind.Updated, model.LastOutcome);
    }
}
