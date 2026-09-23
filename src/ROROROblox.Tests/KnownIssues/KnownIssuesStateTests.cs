using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core.KnownIssues;
using static ROROROblox.Tests.KnownIssues.KnownIssueBuilder;

namespace ROROROblox.Tests.KnownIssues;

public class KnownIssuesStateTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Noon);
    private readonly CapturingLogger<KnownIssuesState> _log = new();

    private KnownIssuesState State() => new(_clock, _log);

    private static KnownIssuesDocument Doc(params KnownIssue[] issues) => new(1, issues);

    private sealed class ScriptedFeed : IKnownIssuesFeed
    {
        private int _inFlight;

        public KnownIssuesDocument? Saved { get; init; }

        public Queue<Func<Task<KnownIssuesFetch>>> Fetches { get; } = new();

        public int MostAtOnce { get; private set; }

        public KnownIssuesDocument? LoadCache() => Saved;

        public async Task<KnownIssuesFetch> FetchAsync(CancellationToken cancellationToken = default)
        {
            var now = Interlocked.Increment(ref _inFlight);
            MostAtOnce = Math.Max(MostAtOnce, now);
            try
            {
                return await Fetches.Dequeue()();
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public void Returns(KnownIssuesRefreshKind kind, KnownIssuesDocument? doc = null) =>
            Fetches.Enqueue(() => Task.FromResult(new KnownIssuesFetch(kind, doc, [])));
    }

    [Fact]
    public void TheSavedCopyIsShownFirst_NotYetChecked()
    {
        var state = State();
        KnownIssuesSnapshot? raised = null;
        state.Changed += (_, s) => raised = s;

        state.LoadSavedCopy(new ScriptedFeed { Saved = Doc(Issue("a"), Issue("b", notify: false)) });

        Assert.Equal(KnownIssuesSource.SavedCopy, state.Current.Source);
        Assert.Null(state.Current.CheckedAt);
        Assert.Equal(2, state.Current.Issues.Count);
        Assert.Same(state.Current, raised);
        Assert.Contains(_log.Snapshot(), l => l == "[Information] Known issues: 2 entries, 1 with a notice, from the saved copy.");
    }

    [Fact]
    public void WithNoSavedCopyNothingChanges()
    {
        var state = State();
        var raised = false;
        state.Changed += (_, _) => raised = true;

        state.LoadSavedCopy(new ScriptedFeed());

        Assert.Same(KnownIssuesSnapshot.Empty, state.Current);
        Assert.False(raised);
    }

    [Fact]
    public async Task AnUpdateReplacesTheListAndStampsTheCheck()
    {
        var state = State();
        var feed = new ScriptedFeed();
        feed.Returns(KnownIssuesRefreshKind.Updated, Doc(Issue("a")));

        var kind = await state.RefreshAsync(feed);

        Assert.Equal(KnownIssuesRefreshKind.Updated, kind);
        Assert.Equal(KnownIssuesSource.Release, state.Current.Source);
        Assert.Equal(Noon, state.Current.CheckedAt);
        Assert.Contains(_log.Snapshot(), l => l == "[Information] Known issues: 1 entries, 1 with a notice, from the release.");
    }

    [Fact]
    public async Task AFailureKeepsTheListButStillStampsTheCheck_AndSaysWhy()
    {
        var state = State();
        var feed = new ScriptedFeed { Saved = Doc(Issue("a")) };
        state.LoadSavedCopy(feed);
        feed.Returns(KnownIssuesRefreshKind.NetworkFailed);

        await state.RefreshAsync(feed);

        Assert.Equal("a", Assert.Single(state.Current.Issues).Id);
        Assert.Equal(KnownIssuesSource.SavedCopy, state.Current.Source);
        Assert.Equal(Noon, state.Current.CheckedAt);
        Assert.Contains(_log.Snapshot(), l => l == "[Information] Known issues: kept 1 entries from SavedCopy: NetworkFailed.");
    }

    [Fact]
    public async Task AnEmptyPublishedListRetiresEverything()
    {
        var state = State();
        var feed = new ScriptedFeed { Saved = Doc(Issue("a")) };
        state.LoadSavedCopy(feed);
        feed.Returns(KnownIssuesRefreshKind.Updated, Doc());

        await state.RefreshAsync(feed);

        Assert.Empty(state.Current.Issues);
    }

    /// <summary>Review Focus 4: the startup download is still running when a tick fires.</summary>
    [Fact]
    public async Task OverlappingRefreshesRunOneAtATime()
    {
        var state = State();
        var feed = new ScriptedFeed();
        var first = new TaskCompletionSource<KnownIssuesFetch>();
        var second = new TaskCompletionSource<KnownIssuesFetch>();
        feed.Fetches.Enqueue(() => first.Task);
        feed.Fetches.Enqueue(() => second.Task);

        var a = state.RefreshAsync(feed);
        var b = state.RefreshAsync(feed);
        first.SetResult(new KnownIssuesFetch(KnownIssuesRefreshKind.NetworkFailed, null, []));
        second.SetResult(new KnownIssuesFetch(KnownIssuesRefreshKind.Updated, Doc(Issue("a")), []));
        await Task.WhenAll(a, b);

        Assert.Equal(1, feed.MostAtOnce);
        Assert.Equal("a", Assert.Single(state.Current.Issues).Id);
    }
}
