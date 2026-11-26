using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Feeds;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Feeds;

namespace ROROROblox.Tests.Feeds;

/// <summary>
/// The feeds coordinator on a fake clock. Time moves in 30-second steps with every pass settled between
/// them, so a test sees exactly the passes the real timer would run.
/// </summary>
public class RobloxFeedsTests
{
    private static readonly Version Latest = new(0, 748, 0, 7481526);
    private static readonly RobloxUpdateState PendingNewer = new(RobloxUpdateKind.Pending, RobloxUpdateDirection.Newer, Latest);
    private static readonly RobloxUpdateState Current = new(RobloxUpdateKind.Current, RobloxUpdateDirection.None, Latest);

    private sealed class FakeProbe : IRobloxUpdateProbe
    {
        private int _reads;
        private int _compares;

        public int StateReads => Volatile.Read(ref _reads);

        public int Compares => Volatile.Read(ref _compares);

        public Func<Task<RobloxUpdateState>> OnRead { get; set; } = () => Task.FromResult(RobloxUpdateState.Unknown);

        public Func<Version, RobloxUpdateState> OnCompare { get; set; } = v => new RobloxUpdateState(RobloxUpdateKind.Current, RobloxUpdateDirection.None, v);

        public Task<RobloxUpdateState> GetUpdateStateAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reads);
            return OnRead();
        }

        public RobloxUpdateState CompareInstalledTo(Version latest)
        {
            Interlocked.Increment(ref _compares);
            return OnCompare(latest);
        }

        public bool IsInstallerRunning() => false;

        public Task<bool> IsUpdatePendingAsync(CancellationToken ct = default) => Task.FromResult(false);

        public bool IsUpdateChurnActive() => false;
    }

    private sealed class FakeStatusApi : IRobloxStatusApi
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public Func<Task<RobloxStatusRead>> OnRead { get; set; } = () => Task.FromResult(Ok(Snap()));

        public Task<RobloxStatusRead> ReadAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reads);
            return OnRead();
        }
    }

    private sealed class FakeStrap : IBloxstrapDetector
    {
        public bool Handles { get; set; }

        public bool IsBloxstrapHandler() => Handles;

        public bool IsStrapHandlingLaunches() => Handles;
    }

    private static RobloxStatusRead Ok(RobloxStatusSnapshot snapshot) => new(FeedOutcome.Ok, snapshot);

    private static RobloxStatusRead Failed(FeedOutcome outcome) => new(outcome, null);

    /// <summary>Every watched pair, operational, except the Games row at <paramref name="gamesCode"/>.</summary>
    private static RobloxStatusSnapshot Snap(int gamesCode = 100) => new(
        RobloxStatusWatch.Roblox
            .Select(w => new StatusCell(w.ComponentId, w.ContainerId, w.Row == RobloxStatusRow.Games ? gamesCode : 100))
            .ToList());

    private sealed class Rig : IDisposable
    {
        public Rig(FeedOptions? options = null)
        {
            Feeds = new RobloxFeeds(() => Probe, () => Api, Strap, Clock, Log, options);
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));

        public FakeProbe Probe { get; } = new();

        public FakeStatusApi Api { get; } = new();

        public FakeStrap Strap { get; } = new();

        public CapturingLogger<RobloxFeeds> Log { get; } = new();

        public RobloxFeeds Feeds { get; }

        public int Changes;

        public Rig Started()
        {
            Feeds.Changed += (_, _) => Interlocked.Increment(ref Changes);
            Feeds.Start();
            return this;
        }

        /// <summary>Advances the clock a tick at a time, letting every pass finish before the next.</summary>
        public async Task RunAsync(TimeSpan span)
        {
            var step = TimeSpan.FromSeconds(30);
            while (span > TimeSpan.Zero)
            {
                var now = span < step ? span : step;
                Clock.Advance(now);
                await Feeds.WhenIdleAsync();
                span -= now;
            }
        }

        public async Task ShowAsync()
        {
            Feeds.SetWindowVisible(true);
            await Feeds.WhenIdleAsync();
        }

        public void Dispose() => Feeds.Dispose();
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "the condition was not reached in time");
            await Task.Delay(1);
        }
    }

    [Fact]
    public async Task Start_ReadsNothing_EvenWithTheWindowAlreadyVisible()
    {
        using var rig = new Rig();
        rig.Feeds.SetWindowVisible(true);

        rig.Feeds.Start();
        await rig.Feeds.WhenIdleAsync();

        Assert.Equal(0, rig.Probe.StateReads);
        Assert.Equal(0, rig.Api.Reads);
        Assert.Equal(0, rig.Probe.Compares);
    }

    [Fact]
    public async Task NothingIsRead_UntilThirtySecondsAfterTheWindowIsFirstVisible()
    {
        using var rig = new Rig().Started();
        await rig.RunAsync(TimeSpan.FromSeconds(5));
        await rig.ShowAsync();

        await rig.RunAsync(TimeSpan.FromSeconds(25)); // the tick 25 s after it was shown

        Assert.Equal(0, rig.Probe.StateReads);
        Assert.Equal(0, rig.Api.Reads);

        await rig.RunAsync(TimeSpan.FromSeconds(30)); // 55 s after

        Assert.Equal(1, rig.Probe.StateReads);
        Assert.Equal(1, rig.Api.Reads);
    }

    [Fact]
    public async Task NothingIsRead_WhileTheWindowIsNeverVisible()
    {
        using var rig = new Rig().Started();

        await rig.RunAsync(TimeSpan.FromHours(2));

        Assert.Equal(0, rig.Probe.StateReads);
        Assert.Equal(0, rig.Api.Reads);
    }

    [Fact]
    public async Task ReadsAllowedFrom_IsTheFirstVisibleInstantPlusTheSettle()
    {
        using var rig = new Rig().Started();
        Assert.Null(rig.Feeds.ReadsAllowedFrom);

        await rig.RunAsync(TimeSpan.FromSeconds(10));
        var shownAt = rig.Clock.GetUtcNow();
        await rig.ShowAsync();
        await rig.RunAsync(TimeSpan.FromMinutes(5));
        rig.Feeds.SetWindowVisible(false);
        rig.Feeds.SetWindowVisible(true);
        await rig.Feeds.WhenIdleAsync();

        Assert.Equal(shownAt.AddSeconds(30), rig.Feeds.ReadsAllowedFrom);
    }

    [Fact]
    public async Task Update_IsReadEveryThirtyMinutes_WhileVisible()
    {
        using var rig = new Rig().Started();
        await rig.ShowAsync();
        await rig.RunAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, rig.Probe.StateReads);

        await rig.RunAsync(TimeSpan.FromMinutes(29).Add(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, rig.Probe.StateReads);

        await rig.RunAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, rig.Probe.StateReads);
    }

    [Fact]
    public async Task Update_IsNeverReadUnderAStrap_AndAPassThatSeesOneSetsTheStateToUnknown()
    {
        using var rig = new Rig().Started();
        rig.Feeds.ReportUpdateState(PendingNewer); // a notice read before the strap arrived
        Assert.Equal(RobloxUpdateKind.Pending, rig.Feeds.Update.Kind);
        rig.Strap.Handles = true;
        await rig.ShowAsync();

        await rig.RunAsync(TimeSpan.FromHours(2));

        Assert.Equal(0, rig.Probe.StateReads);
        Assert.Equal(RobloxUpdateState.Unknown, rig.Feeds.Update);
        Assert.True(rig.Api.Reads > 0, "a strap changes nothing about the status lane");
    }

    [Fact]
    public async Task Status_IsReadEveryTenMinutes_WhileVisible()
    {
        using var rig = new Rig().Started();
        await rig.ShowAsync();
        await rig.RunAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, rig.Api.Reads);

        await rig.RunAsync(TimeSpan.FromMinutes(9).Add(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, rig.Api.Reads);

        await rig.RunAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, rig.Api.Reads);
    }

    [Theory]
    [InlineData(FeedOutcome.Refused, 60)]
    [InlineData(FeedOutcome.HttpError, 10)]
    [InlineData(FeedOutcome.Timeout, 10)]
    public async Task Status_NextRead_WaitsForTheIntervalTheOutcomeEarned(FeedOutcome outcome, int minutes)
    {
        using var rig = new Rig().Started();
        rig.Api.OnRead = () => Task.FromResult(Failed(outcome));
        await rig.ShowAsync();
        await rig.RunAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, rig.Api.Reads);

        await rig.RunAsync(TimeSpan.FromMinutes(minutes).Subtract(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, rig.Api.Reads);

        await rig.RunAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(2, rig.Api.Reads);
    }

    [Fact]
    public async Task Status_AnEarlyRead_IsHonouredOnce_NoSoonerThanSixtySecondsAfterTheLastAttempt()
    {
        using var rig = new Rig().Started();
        await rig.ShowAsync();
        await rig.RunAsync(TimeSpan.FromSeconds(30)); // the read at +30 s
        Assert.Equal(1, rig.Api.Reads);

        await rig.RunAsync(TimeSpan.FromSeconds(10));
        rig.Feeds.RequestEarlyStatusRead();
        await rig.RunAsync(TimeSpan.FromSeconds(20)); // the tick 30 s after the last attempt
        Assert.Equal(1, rig.Api.Reads);

        await rig.RunAsync(TimeSpan.FromSeconds(30)); // 60 s after
        Assert.Equal(2, rig.Api.Reads);

        await rig.RunAsync(TimeSpan.FromMinutes(5)); // the request was spent
        Assert.Equal(2, rig.Api.Reads);
    }

    [Fact]
    public async Task BecomingVisible_RunsTheScheduleAtOnce_WithoutWaitingForATick()
    {
        using var rig = new Rig().Started();
        await rig.ShowAsync();
        await rig.RunAsync(TimeSpan.FromSeconds(30));
        rig.Feeds.SetWindowVisible(false);
        await rig.RunAsync(TimeSpan.FromMinutes(31));
        Assert.Equal(1, rig.Probe.StateReads);
        Assert.Equal(1, rig.Api.Reads);

        rig.Feeds.SetWindowVisible(true);
        await rig.Feeds.WhenIdleAsync(); // no clock movement: the show itself ran the pass

        Assert.Equal(2, rig.Probe.StateReads);
        Assert.Equal(2, rig.Api.Reads);
    }

    [Fact]
    public void Pending_WindowHidden_AClientAttaches_TheVersionsNowMatch_IsCurrentWithZeroRequests()
    {
        using var rig = new Rig().Started();
        rig.Probe.OnCompare = v => new RobloxUpdateState(RobloxUpdateKind.Current, RobloxUpdateDirection.None, v);
        rig.Feeds.ReportUpdateState(PendingNewer);
        var before = rig.Changes;

        rig.Feeds.NoteClientAttached();

        Assert.Equal(Current, rig.Feeds.Update);
        Assert.Equal(before + 1, rig.Changes);
        Assert.Equal(1, rig.Probe.Compares);
        Assert.Equal(0, rig.Probe.StateReads);
        Assert.Equal(0, rig.Api.Reads);
    }

    [Fact]
    public async Task Pending_WindowHidden_ATimerPassReComparesToo_AndStillAsksNothing()
    {
        using var rig = new Rig().Started();
        rig.Feeds.ReportUpdateState(PendingNewer);

        await rig.RunAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(RobloxUpdateKind.Current, rig.Feeds.Update.Kind);
        Assert.Equal(0, rig.Probe.StateReads);
        Assert.Equal(0, rig.Api.Reads);
    }

    [Fact]
    public void Pending_AClientAttachesButTheVersionsStillDiffer_StaysPending()
    {
        using var rig = new Rig().Started();
        rig.Probe.OnCompare = _ => PendingNewer;
        rig.Feeds.ReportUpdateState(PendingNewer);

        rig.Feeds.NoteClientAttached();

        Assert.Equal(PendingNewer, rig.Feeds.Update);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAttach_WhileNothingIsPending_ReadsNothing(bool windowVisible)
    {
        using var rig = new Rig().Started();
        if (windowVisible)
        {
            await rig.ShowAsync();
        }

        rig.Feeds.NoteClientAttached(); // the startup re-attach scan: the state is still Unknown

        Assert.Equal(0, rig.Probe.Compares);
        Assert.Equal(0, rig.Probe.StateReads);
        Assert.Equal(0, rig.Api.Reads);

        rig.Feeds.ReportUpdateState(Current);
        rig.Feeds.NoteClientAttached();

        Assert.Equal(0, rig.Probe.Compares);
    }

    [Fact]
    public async Task AFailedUpdateRead_GivesUnknown_AndTheNextGoodReadBringsTheNoticeBack()
    {
        using var rig = new Rig().Started();
        rig.Probe.OnCompare = _ => PendingNewer;
        rig.Probe.OnRead = () => Task.FromResult(PendingNewer);
        await rig.ShowAsync();
        await rig.RunAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(PendingNewer, rig.Feeds.Update);

        rig.Probe.OnRead = () => Task.FromResult(RobloxUpdateState.Unknown);
        await rig.RunAsync(TimeSpan.FromMinutes(30));
        Assert.Equal(RobloxUpdateState.Unknown, rig.Feeds.Update);

        rig.Probe.OnRead = () => Task.FromResult(PendingNewer);
        await rig.RunAsync(TimeSpan.FromMinutes(30));
        Assert.Equal(PendingNewer, rig.Feeds.Update);
    }

    [Fact]
    public async Task AnUpdateProbeThatThrows_GivesUnknown_AndNothingEscapes()
    {
        using var rig = new Rig().Started();
        rig.Probe.OnRead = () => throw new InvalidOperationException("boom");
        rig.Feeds.ReportUpdateState(PendingNewer);
        rig.Probe.OnCompare = _ => PendingNewer;
        await rig.ShowAsync();

        await rig.RunAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(RobloxUpdateState.Unknown, rig.Feeds.Update);
    }

    [Fact]
    public async Task AFailedStatusRead_KeepsTheReadingUntilItIsThirtyMinutesOld_ThenNone_EvenWhileHidden()
    {
        using var rig = new Rig().Started();
        rig.Api.OnRead = () => Task.FromResult(Ok(Snap(gamesCode: 500)));
        await rig.ShowAsync();
        await rig.RunAsync(TimeSpan.FromSeconds(30)); // the good read, at +30 s
        Assert.Equal(new RobloxStatusReading(RobloxStatusRow.Games, RobloxStatusLevel.Outage, 0), rig.Feeds.Status);

        rig.Api.OnRead = () => Task.FromResult(Failed(FeedOutcome.Timeout));
        await rig.RunAsync(TimeSpan.FromMinutes(10)); // a failed read at +10:30
        Assert.Equal(2, rig.Api.Reads);
        Assert.Equal(RobloxStatusLevel.Outage, rig.Feeds.Status.Level);

        rig.Feeds.SetWindowVisible(false);
        await rig.RunAsync(TimeSpan.FromMinutes(19)); // 29:30 after the good read
        Assert.Equal(2, rig.Api.Reads);
        Assert.Equal(RobloxStatusLevel.Outage, rig.Feeds.Status.Level);

        var before = rig.Changes;
        await rig.RunAsync(TimeSpan.FromSeconds(60)); // 30:30, hidden the whole way

        Assert.Equal(RobloxStatusReading.None, rig.Feeds.Status);
        Assert.Equal(before + 1, rig.Changes);
        Assert.Equal(2, rig.Api.Reads);
    }

    [Fact]
    public async Task StatusDisabled_TwoHoursVisible_MakesZeroStatusRequests_AndUpdateStillRuns()
    {
        using var rig = new Rig();
        rig.Feeds.SetStatusEnabled(false);
        rig.Feeds.Start();
        rig.Feeds.RequestEarlyStatusRead();
        await rig.ShowAsync();

        await rig.RunAsync(TimeSpan.FromHours(2));

        Assert.Equal(0, rig.Api.Reads);
        Assert.Equal(4, rig.Probe.StateReads);
    }

    [Fact]
    public async Task SwitchingStatusOff_ClearsTheReadingAtOnce_AndRaisesChanged()
    {
        using var rig = new Rig().Started();
        rig.Api.OnRead = () => Task.FromResult(Ok(Snap(gamesCode: 400)));
        await rig.ShowAsync();
        await rig.RunAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(RobloxStatusLevel.PartialOutage, rig.Feeds.Status.Level);
        var before = rig.Changes;

        rig.Feeds.SetStatusEnabled(false);

        Assert.Equal(RobloxStatusReading.None, rig.Feeds.Status);
        Assert.Equal(before + 1, rig.Changes);
    }

    [Fact]
    public async Task SwitchingStatusBackOn_ReadsOnceAfterAFreshThirtySecondSettle()
    {
        using var rig = new Rig();
        rig.Feeds.SetStatusEnabled(false);
        rig.Feeds.Start();
        await rig.ShowAsync();
        await rig.RunAsync(TimeSpan.FromHours(1));
        await rig.RunAsync(TimeSpan.FromSeconds(10));

        rig.Feeds.SetStatusEnabled(true);
        await rig.RunAsync(TimeSpan.FromSeconds(20)); // the tick 20 s after

        Assert.Equal(0, rig.Api.Reads);

        await rig.RunAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(1, rig.Api.Reads);
    }

    [Fact]
    public async Task ChangedThatThrows_NeverReachesTheTimer_AndTheStateStillLands()
    {
        using var rig = new Rig();
        rig.Feeds.Changed += (_, _) => throw new InvalidOperationException("subscriber");
        rig.Feeds.Start();
        rig.Api.OnRead = () => Task.FromResult(Ok(Snap(gamesCode: 300)));
        rig.Probe.OnRead = () => Task.FromResult(PendingNewer);
        rig.Probe.OnCompare = _ => PendingNewer;
        await rig.ShowAsync();

        rig.Feeds.ReportUpdateState(PendingNewer);
        await rig.RunAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(RobloxStatusLevel.Degraded, rig.Feeds.Status.Level);
        Assert.Equal(PendingNewer, rig.Feeds.Update);
        Assert.Contains(rig.Log.Snapshot(), l => l.Contains("subscriber threw"));
    }

    [Fact]
    public async Task ASecondTickDuringARead_StartsNoSecondRead()
    {
        using var rig = new Rig().Started();
        var release = new TaskCompletionSource<RobloxUpdateState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiRelease = new TaskCompletionSource<RobloxStatusRead>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Probe.OnRead = () => release.Task;
        rig.Api.OnRead = () => apiRelease.Task;
        rig.Feeds.SetWindowVisible(true);

        rig.Clock.Advance(TimeSpan.FromSeconds(30)); // the timer's pass, parked in both reads
        await UntilAsync(() => rig.Probe.StateReads == 1 && rig.Api.Reads == 1);

        await rig.Feeds.TickAsync();
        await rig.Feeds.TickAsync();

        Assert.Equal(1, rig.Probe.StateReads);
        Assert.Equal(1, rig.Api.Reads);

        // Both intervals pass while the reads are still hung, an early read is asked for, and the
        // lane's own gate (not the schedule) is what holds the next read back.
        rig.Clock.Advance(TimeSpan.FromMinutes(31));
        rig.Feeds.RequestEarlyStatusRead();
        await rig.Feeds.TickAsync();

        Assert.Equal(1, rig.Probe.StateReads);
        Assert.Equal(1, rig.Api.Reads);

        release.SetResult(RobloxUpdateState.Unknown);
        apiRelease.SetResult(Ok(Snap()));
        await rig.Feeds.WhenIdleAsync();
    }

    [Fact]
    public async Task AMissingWatchedRow_WarnsOncePerRun_WithTheRowsOwnName()
    {
        using var rig = new Rig().Started();
        rig.Api.OnRead = () => Task.FromResult(Ok(new RobloxStatusSnapshot([])));
        await rig.ShowAsync();

        await rig.RunAsync(TimeSpan.FromMinutes(25)); // three reads

        Assert.True(rig.Api.Reads >= 3);
        var warnings = rig.Log.Snapshot().Where(l => l.StartsWith("[Warning]", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, warnings.Count);
        foreach (var row in Enum.GetValues<RobloxStatusRow>())
        {
            Assert.Single(warnings, l => l.EndsWith($"watched row {row} is not on the page", StringComparison.Ordinal));
        }

        Assert.Equal(RobloxStatusReading.None, rig.Feeds.Status);
    }

    [Fact]
    public async Task EachRead_LogsOneDebugLine_OfEnumsAndNothingElse()
    {
        using var rig = new Rig().Started();
        rig.Api.OnRead = () => Task.FromResult(Ok(Snap(gamesCode: 500)));
        rig.Probe.OnRead = () => Task.FromResult(PendingNewer);
        rig.Probe.OnCompare = _ => PendingNewer;
        await rig.ShowAsync();

        await rig.RunAsync(TimeSpan.FromSeconds(30));

        var lines = rig.Log.Snapshot();
        Assert.Single(lines, "[Debug] roblox-status read: Ok");
        Assert.Single(lines, "[Debug] roblox-update read: Pending Newer");
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public async Task AfterDispose_NothingIsRead_AndTheTimerIsGone()
    {
        using var rig = new Rig().Started();
        await rig.ShowAsync();
        rig.Feeds.Dispose();

        await rig.RunAsync(TimeSpan.FromHours(1));
        rig.Feeds.SetWindowVisible(false);
        rig.Feeds.SetWindowVisible(true);
        await rig.Feeds.WhenIdleAsync();

        Assert.Equal(0, rig.Probe.StateReads);
        Assert.Equal(0, rig.Api.Reads);
    }
}
