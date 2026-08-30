using ROROROblox.Core;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The decorator exists so there is still exactly ONE call path into history (spec §3.2).
/// F-121 happened because a fix landed at one call site while a second kept the old form, with a
/// comment claiming they matched — recording stats from a second call site in MainViewModel would
/// have created that same shape on day one.
/// </summary>
public class StatsRecordingSessionHistoryStoreTests
{
    private sealed class RecordingInner : ISessionHistoryStore
    {
        public readonly List<LaunchSession> Added = new();
        public readonly List<Guid> Ended = new();
        public int ClearCalls;

        public Task<IReadOnlyList<LaunchSession>> ListAsync()
            => Task.FromResult<IReadOnlyList<LaunchSession>>(Added);
        public Task AddAsync(LaunchSession s) { Added.Add(s); return Task.CompletedTask; }
        public Task MarkEndedAsync(Guid id, DateTimeOffset at, string? hint = null)
        { Ended.Add(id); return Task.CompletedTask; }
        public readonly List<(Guid Id, string Hint)> Outcomes = new();
        public Task MarkOutcomeAsync(Guid id, string hint)
        { Outcomes.Add((id, hint)); return Task.CompletedTask; }
        public Task ClearAsync() { ClearCalls++; return Task.CompletedTask; }
    }

    private sealed class ThrowingStats : ISessionStatsStore
    {
        public Task ApplyAsync(StatsEvent e) => throw new InvalidOperationException("disk on fire");
        public Task<SessionStats> ReadAsync() => Task.FromResult(SessionStats.Empty);
        public Task ClearAsync() => throw new InvalidOperationException("disk on fire");
    }

    private sealed class SpyStats : ISessionStatsStore
    {
        public readonly List<StatsEvent> Events = new();
        public int ClearCalls;
        public Task ApplyAsync(StatsEvent e) { Events.Add(e); return Task.CompletedTask; }
        public Task<SessionStats> ReadAsync() => Task.FromResult(SessionStats.Empty);
        public Task ClearAsync() { ClearCalls++; Events.Clear(); return Task.CompletedTask; }
    }

    private static LaunchSession Sample(Guid? account = null, DateTimeOffset? launchedAt = null) => new(
        Id: Guid.NewGuid(),
        AccountId: account ?? Guid.NewGuid(),
        AccountDisplayName: "Pokey",
        AccountAvatarUrl: null,
        GameName: "Pet Sim 99",
        PlaceId: 12345L,
        IsPrivateServer: false,
        LaunchedAtUtc: launchedAt ?? DateTimeOffset.UtcNow,
        EndedAtUtc: null,
        OutcomeHint: null);

    [Fact]
    public async Task EveryCallReachesTheInnerStore()
    {
        var inner = new RecordingInner();
        var sut = new StatsRecordingSessionHistoryStore(inner, new SpyStats());
        var session = Sample();

        await sut.AddAsync(session);
        await sut.MarkEndedAsync(session.Id, DateTimeOffset.UtcNow);
        await sut.ClearAsync();
        await sut.ListAsync();

        Assert.Single(inner.Added);
        Assert.Single(inner.Ended);
        Assert.Equal(1, inner.ClearCalls);
    }

    [Fact]
    public async Task AddRecordsALaunch()
    {
        var stats = new SpyStats();
        var sut = new StatsRecordingSessionHistoryStore(new RecordingInner(), stats);

        await sut.AddAsync(Sample());

        Assert.Contains(stats.Events, e => e is StatsEvent.LaunchRecorded);
    }

    [Fact]
    public async Task EndingAKnownSessionRecordsItsDuration()
    {
        var inner = new RecordingInner();
        var stats = new SpyStats();
        var sut = new StatsRecordingSessionHistoryStore(inner, stats);
        var started = DateTimeOffset.UtcNow.AddMinutes(-30);
        var session = Sample(launchedAt: started);
        await sut.AddAsync(session);

        await sut.MarkEndedAsync(session.Id, started.AddMinutes(30));

        var ended = Assert.Single(stats.Events.OfType<StatsEvent.SessionEnded>());
        Assert.Equal(started, ended.StartedUtc);
        Assert.Equal(session.AccountId, ended.AccountId);
    }

    [Fact]
    public async Task EndingAPrunedSessionCountsItRatherThanInventingADuration()
    {
        // The row fell off the 100-row window before its client closed. We cannot know how long
        // it ran, so it is counted as missing rather than guessed at (spec §5).
        var stats = new SpyStats();
        var sut = new StatsRecordingSessionHistoryStore(new RecordingInner(), stats);

        await sut.MarkEndedAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Contains(stats.Events, e => e is StatsEvent.SessionMissingEnd);
        Assert.DoesNotContain(stats.Events, e => e is StatsEvent.SessionEnded);
    }

    [Fact]
    public async Task AStatsFailureDoesNotPreventTheHistoryWrite()
    {
        // A swallowed inner call would be a data-loss bug wearing a feature's clothes (§3.2).
        var inner = new RecordingInner();
        var sut = new StatsRecordingSessionHistoryStore(inner, new ThrowingStats());

        await sut.AddAsync(Sample());

        Assert.Single(inner.Added);
    }

    [Fact]
    public async Task AStatsFailureDoesNotPreventMarkingASessionEnded()
    {
        var inner = new RecordingInner();
        var sut = new StatsRecordingSessionHistoryStore(inner, new ThrowingStats());
        var session = Sample();
        await sut.AddAsync(session);

        await sut.MarkEndedAsync(session.Id, DateTimeOffset.UtcNow);

        Assert.Single(inner.Ended);
    }

    [Fact]
    public async Task AStatsFailureDoesNotPreventClearingHistory()
    {
        var inner = new RecordingInner();
        var sut = new StatsRecordingSessionHistoryStore(inner, new ThrowingStats());

        await sut.ClearAsync();

        Assert.Equal(1, inner.ClearCalls);
    }

    [Fact]
    public async Task ClearingHistoryAlsoClearsStats()
    {
        // Otherwise "clear my history" leaves lifetime totals standing, which reads as the
        // confirmation dialog having lied.
        var stats = new SpyStats();
        var sut = new StatsRecordingSessionHistoryStore(new RecordingInner(), stats);
        await sut.AddAsync(Sample());

        await sut.ClearAsync();

        Assert.Equal(1, stats.ClearCalls);
        Assert.Empty(stats.Events);
    }

    [Fact]
    public async Task ListPassesThroughUnchanged()
    {
        var inner = new RecordingInner();
        var sut = new StatsRecordingSessionHistoryStore(inner, new SpyStats());
        await sut.AddAsync(Sample());

        var rows = await sut.ListAsync();

        Assert.Single(rows);
    }
}
