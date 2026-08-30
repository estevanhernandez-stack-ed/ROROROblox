using System.IO;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// The history end-stamp follows the row's both-signals rule, and a launch that never ran has no
/// duration.
///
/// <para><b>Why this file exists (2026-08-30).</b> <c>RecordSessionEndAsync</c> fired on
/// <c>ProcessExited</c> unconditionally since <c>009866a</c> (2026-05-04), sixteen days before the
/// v1.5 rule that a row is Closed only when presence AND process tracking agree (2026-05-20), and
/// nobody revisited it. Two consequences reached the v1.23 stats page: a bootstrapper-respawned
/// client ended its history row and its uptime at the OLD pid's death while the row correctly kept
/// saying "In &lt;game&gt;", and a launch whose client never attached went through
/// <c>MarkEndedAsync</c> with the failure time as its end, a 30-120 s phantom session per attempt.
/// No test had ever driven the end-stamp path: <c>MainViewModelTests</c>' history fake throws on
/// every member and the fire-and-forget call swallowed it.</para>
///
/// <para><b>What this does NOT prove.</b> That the History page renders a hinted, end-less row the
/// way a user expects (that is <c>SessionHistoryPage</c> code-behind), or that a client adopted by
/// the orphan sweeper after an attach failure re-opens its row (it does not; the row keeps "Never
/// connected", the same outcome as before this change).</para>
/// </summary>
public class SessionHistoryEndStampTests : IDisposable
{
    private readonly string _historyPath = Path.Combine(Path.GetTempPath(), $"rororo-endstamp-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        try { if (File.Exists(_historyPath)) File.Delete(_historyPath); } catch { }
    }

    private static LaunchSession Session(Guid? accountId = null, DateTimeOffset? launchedAt = null, DateTimeOffset? endedAt = null, string? hint = null) =>
        new(
            Id: Guid.NewGuid(),
            AccountId: accountId ?? Guid.NewGuid(),
            AccountDisplayName: "Pokey",
            AccountAvatarUrl: null,
            GameName: "Pet Sim 99",
            PlaceId: 8737899170L,
            IsPrivateServer: false,
            LaunchedAtUtc: launchedAt ?? DateTimeOffset.UtcNow,
            EndedAtUtc: endedAt,
            OutcomeHint: hint);

    private sealed class RecordingStats : ISessionStatsStore
    {
        public readonly List<StatsEvent> Events = new();
        public Task ApplyAsync(StatsEvent e) { Events.Add(e); return Task.CompletedTask; }
        public Task<SessionStats> ReadAsync() => Task.FromResult(SessionStats.Empty);
        public Task ClearAsync() => Task.CompletedTask;
    }

    private sealed class ListOnlyHistory(IReadOnlyList<LaunchSession> rows) : ISessionHistoryStore
    {
        public Task<IReadOnlyList<LaunchSession>> ListAsync() => Task.FromResult(rows);
        public Task AddAsync(LaunchSession session) => throw new NotSupportedException();
        public Task MarkEndedAsync(Guid sessionId, DateTimeOffset endedAtUtc, string? outcomeHint = null) => throw new NotSupportedException();
        public Task MarkOutcomeAsync(Guid sessionId, string outcomeHint) => throw new NotSupportedException();
        public Task ClearAsync() => throw new NotSupportedException();
    }

    // ---------------------------------------------------------------- store

    [Fact]
    public async Task MarkOutcomeAsync_SetsTheHintAndLeavesTheEndNull()
    {
        using var store = new SessionHistoryStore(_historyPath);
        var session = Session();
        await store.AddAsync(session);

        await store.MarkOutcomeAsync(session.Id, "Never connected");

        var row = Assert.Single(await store.ListAsync());
        Assert.Equal("Never connected", row.OutcomeHint);
        Assert.Null(row.EndedAtUtc);
        Assert.Null(row.Duration);
    }

    [Fact]
    public async Task MarkOutcomeAsync_OnAPrunedRow_IsANoOp()
    {
        using var store = new SessionHistoryStore(_historyPath);
        await store.AddAsync(Session());

        await store.MarkOutcomeAsync(Guid.NewGuid(), "Never connected");

        var row = Assert.Single(await store.ListAsync());
        Assert.Null(row.OutcomeHint);
    }

    // ------------------------------------------------------------ decorator

    [Fact]
    public async Task TheStatsDecoratorForwardsAnOutcome_AndFeedsTheRollupNothing()
    {
        using var inner = new SessionHistoryStore(_historyPath);
        var stats = new RecordingStats();
        var sut = new StatsRecordingSessionHistoryStore(inner, stats);
        var session = Session();
        await sut.AddAsync(session);

        await sut.MarkOutcomeAsync(session.Id, "Never connected");

        var row = Assert.Single(await sut.ListAsync());
        Assert.Equal("Never connected", row.OutcomeHint);
        Assert.Null(row.EndedAtUtc);
        Assert.Single(stats.Events.OfType<StatsEvent.LaunchRecorded>()); // the attempt still counts as a launch
        Assert.DoesNotContain(stats.Events, e => e is StatsEvent.SessionEnded or StatsEvent.SessionMissingEnd);
    }

    // ------------------------------------------------------------- backfill

    [Fact]
    public async Task BackfillCountsAnEndlessRowAsMissing_ButNotOneThatNeverConnected()
    {
        var launched = DateTimeOffset.UtcNow.AddHours(-2);
        var history = new ListOnlyHistory(new[]
        {
            Session(launchedAt: launched, endedAt: launched.AddMinutes(40)),   // a real session
            Session(launchedAt: launched.AddMinutes(5)),                       // in flight / lost its end
            Session(launchedAt: launched.AddMinutes(10), hint: "Never connected"), // never ran
        });
        var stats = new RecordingStats();

        await SessionStatsBackfill.RunOnceAsync(history, stats);

        Assert.Equal(3, stats.Events.OfType<StatsEvent.LaunchRecorded>().Count());
        Assert.Single(stats.Events.OfType<StatsEvent.SessionEnded>());
        Assert.Single(stats.Events.OfType<StatsEvent.SessionMissingEnd>());
    }

    // ---------------------------------------------------------- MainViewModel

    [Fact]
    public async Task AGhostExit_DoesNotEndTheRow_UntilPresenceConfirmsTheClose()
    {
        var history = new MainViewModelTests.RecordingSessionHistoryStore();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher: new MainViewModelTests.RecordingSuccessLauncher(), sessionHistory: history);
        try
        {
            var row = new AccountSummary(await store.AddAsync("Alt", "", "cookie")) { RobloxUserId = 12345 };
            vm.Accounts.Add(row);
            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(PlaceId: 8737899170));
            var session = Assert.Single(history.Added);

            vm.ApplyPresence(new AccountPresenceEventArgs(row.Id, UserPresenceType.InGame, 8737899170, "Pet Sim 99", DateTimeOffset.UtcNow));
            Assert.True(row.InGame);

            // The anti-multilaunch bootstrapper kills the pid we attached to; the client is respawned
            // under a pid we never claimed. Presence still says in-game: the session is NOT over.
            tracker.RaiseExited(new RobloxProcessEventArgs(row.Id, 9001));
            Assert.Empty(history.Ended);
            Assert.Null(row.LastClosedAtUtc);

            // Presence confirms the close: that moment ends the row and the session, once.
            var closedAt = DateTimeOffset.UtcNow;
            vm.ApplyPresence(new AccountPresenceEventArgs(row.Id, UserPresenceType.Offline, null, null, closedAt));
            var ended = Assert.Single(history.Ended);
            Assert.Equal(session.Id, ended.SessionId);
            Assert.Equal(closedAt, ended.EndedAtUtc);
            Assert.Equal(closedAt, row.LastClosedAtUtc);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task AnExitForAnAccountPresenceCannotSee_EndsTheRowAtTheExit()
    {
        var history = new MainViewModelTests.RecordingSessionHistoryStore();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher: new MainViewModelTests.RecordingSuccessLauncher(), sessionHistory: history);
        try
        {
            var row = new AccountSummary(await store.AddAsync("Alt", "", "cookie")); // RobloxUserId null
            vm.Accounts.Add(row);
            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(PlaceId: 8737899170));
            var session = Assert.Single(history.Added);

            var exitedAt = DateTimeOffset.UtcNow;
            tracker.RaiseExited(new RobloxProcessEventArgs(row.Id, 9001) { OccurredAtUtc = exitedAt });

            var ended = Assert.Single(history.Ended);
            Assert.Equal(session.Id, ended.SessionId);
            Assert.Equal(exitedAt, ended.EndedAtUtc);
            Assert.Equal(exitedAt, row.LastClosedAtUtc);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ANeverConnectedLaunch_GetsAnOutcomeAndNoEnd_AndALaterExitCannotEndIt()
    {
        var history = new MainViewModelTests.RecordingSessionHistoryStore();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher: new MainViewModelTests.RecordingSuccessLauncher(), sessionHistory: history);
        try
        {
            var row = new AccountSummary(await store.AddAsync("Alt", "", "cookie"));
            vm.Accounts.Add(row);
            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(PlaceId: 8737899170));
            var session = Assert.Single(history.Added);

            tracker.RaiseAttachFailed(new RobloxProcessEventArgs(row.Id, 0));

            var outcome = Assert.Single(history.Outcomes);
            Assert.Equal(session.Id, outcome.SessionId);
            Assert.Equal("Never connected", outcome.Hint);
            Assert.Empty(history.Ended);

            // Nothing was pending any more, so a stray exit for the account cannot invent an end.
            tracker.RaiseExited(new RobloxProcessEventArgs(row.Id, 9001));
            Assert.Empty(history.Ended);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
