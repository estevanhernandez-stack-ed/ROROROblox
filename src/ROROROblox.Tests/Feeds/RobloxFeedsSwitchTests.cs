using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Feeds;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Feeds;
using ROROROblox.Core.Library;
using ROROROblox.Tests.KnownIssues;

namespace ROROROblox.Tests.Feeds;

/// <summary>
/// The off switch (v1.41 item 9) on the real coordinator, the real pulse service and the real
/// <see cref="MainViewModel"/>, sharing one fake clock. The two halves are covered alone in
/// <c>RobloxFeedsTests</c> and <c>GamePulseServiceTests</c>; this is the one call that drives both.
/// </summary>
public sealed class RobloxFeedsSwitchTests : IDisposable
{
    private static readonly Version Latest = new(0, 749, 0, 7491526);
    private static readonly RobloxUpdateState PendingNewer = new(RobloxUpdateKind.Pending, RobloxUpdateDirection.Newer, Latest);

    private readonly List<string> _accountStores = [];

    public void Dispose()
    {
        foreach (var path in _accountStores.Where(File.Exists))
        {
            File.Delete(path);
        }
    }

    private sealed class FakeProbe : IRobloxUpdateProbe
    {
        public RobloxUpdateState State { get; set; } = RobloxUpdateState.Unknown;

        public int StateReads;

        public Task<RobloxUpdateState> GetUpdateStateAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref StateReads);
            return Task.FromResult(State);
        }

        public RobloxUpdateState CompareInstalledTo(Version latest) => State;

        public bool IsInstallerRunning() => false;

        public Task<bool> IsUpdatePendingAsync(CancellationToken ct = default) => Task.FromResult(State.Kind == RobloxUpdateKind.Pending);

        public bool IsUpdateChurnActive() => false;
    }

    private sealed class FakeStatusApi : IRobloxStatusApi
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public Task<RobloxStatusRead> ReadAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reads);
            var snapshot = new RobloxStatusSnapshot(RobloxStatusWatch.Roblox
                .Select(w => new StatusCell(w.ComponentId, w.ContainerId, w.Row == RobloxStatusRow.Games ? 400 : 100))
                .ToList());
            return Task.FromResult(new RobloxStatusRead(FeedOutcome.Ok, snapshot));
        }
    }

    private sealed class FakePulseApi : IGamePulseApi
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        public Task<GamePulseRead> ReadAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _requests);
            var found = universeIds.ToDictionary(id => id, _ => new GamePulseReading(100, null));
            return Task.FromResult(new GamePulseRead(FeedOutcome.Ok, new GamePulseAnswer(found)));
        }
    }

    private sealed class NoStrap : IBloxstrapDetector
    {
        public bool IsBloxstrapHandler() => false;

        public bool IsStrapHandlingLaunches() => false;
    }

    private sealed class Rig : IDisposable
    {
        public Rig()
        {
            Feeds = new RobloxFeeds(() => Probe, () => Api, new NoStrap(), Clock, NullLogger<RobloxFeeds>.Instance);
            Pulse = new GamePulseService(() => PulseApi, Clock);
        }

        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));

        public FakeProbe Probe { get; } = new();

        public FakeStatusApi Api { get; } = new();

        public FakePulseApi PulseApi { get; } = new();

        public RobloxFeeds Feeds { get; }

        public GamePulseService Pulse { get; }

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

        public void Dispose()
        {
            Feeds.Dispose();
            Pulse.Dispose();
        }
    }

    private MainViewModel Vm(Rig rig)
    {
        var (vm, _, _, path) = MainViewModelTests.Build(
            bloxstrapDetector: new NoStrap(),
            uiDispatcher: new KnownIssuesNoticeModelTests.InlineDispatcher(),
            updateProbe: rig.Probe);
        _accountStores.Add(path);
        vm.RobloxFeeds = rig.Feeds;
        vm.GamePulse = rig.Pulse;
        return vm;
    }

    [Fact]
    public async Task Off_TakesEffectAtOnce_StatusIsNone_EveryPulseIsCleared_BothEventsAreRaised()
    {
        using var rig = new Rig();
        var vm = Vm(rig);
        rig.Feeds.Start();
        vm.SetMainWindowVisible(true);
        rig.Pulse.SetInterest(this, [1, 2]);
        await rig.RunAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(RobloxStatusRow.Games, rig.Feeds.Status.Worst);
        Assert.NotNull(rig.Pulse.TryGet(1));
        Assert.NotNull(rig.Pulse.TryGet(2));
        Assert.True(vm.RobloxLine.HasNotice);

        var feedsChanged = 0;
        var pulseChanged = new List<long>();
        rig.Feeds.Changed += (_, _) => feedsChanged++;
        rig.Pulse.Changed += (_, e) => pulseChanged.AddRange(e.UniverseIds);

        vm.SetRobloxFeedsEnabled(false);

        Assert.Equal(RobloxStatusReading.None, rig.Feeds.Status);
        Assert.Null(rig.Pulse.TryGet(1));
        Assert.Null(rig.Pulse.TryGet(2));
        Assert.Equal(1, feedsChanged);
        Assert.Contains(1L, pulseChanged);
        Assert.Contains(2L, pulseChanged);
        Assert.False(vm.RobloxLine.HasNotice);
    }

    [Fact]
    public async Task StartedOff_TwoHoursVisibleWithInterest_AsksStatusAndPulseNothing()
    {
        using var rig = new Rig();
        var vm = Vm(rig);

        vm.SetRobloxFeedsEnabled(false); // read before Start(), as App does
        rig.Feeds.Start();
        vm.SetMainWindowVisible(true);
        rig.Pulse.SetInterest(this, [1, 2]);
        rig.Feeds.RequestEarlyStatusRead();

        await rig.RunAsync(TimeSpan.FromHours(2));

        Assert.Equal(0, rig.Api.Reads);
        Assert.Equal(0, rig.PulseApi.Requests);
        Assert.False(vm.RobloxLine.HasNotice);
    }

    [Fact]
    public async Task BackOn_StatusReadsAfterTheThirtySecondSettle_AndThePulseAsksAtItsNextPass_OneRequestEach()
    {
        using var rig = new Rig();
        var vm = Vm(rig);
        vm.SetRobloxFeedsEnabled(false);
        rig.Feeds.Start();
        vm.SetMainWindowVisible(true);
        rig.Pulse.SetInterest(this, [1, 2]);
        await rig.RunAsync(TimeSpan.FromHours(1));
        Assert.Equal(0, rig.Api.Reads);
        Assert.Equal(0, rig.PulseApi.Requests);

        vm.SetRobloxFeedsEnabled(true);
        Assert.Equal(0, rig.Api.Reads);
        Assert.Equal(0, rig.PulseApi.Requests); // no burst on the switch itself

        await rig.RunAsync(TimeSpan.FromSeconds(29));
        Assert.Equal(0, rig.Api.Reads); // the settle has not passed

        await rig.RunAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, rig.Api.Reads); // the first tick after it
        Assert.True(vm.RobloxLine.HasNotice);

        await rig.RunAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(1, rig.Api.Reads);
        Assert.Equal(1, rig.PulseApi.Requests); // the pulse's next pass, one request
        Assert.NotNull(rig.Pulse.TryGet(1));
        Assert.True(vm.RobloxLine.HasNotice);
    }

    [Fact]
    public async Task Off_WithAnUpdatePending_TheUpdateNoticeIsStillTheTopNotice()
    {
        using var rig = new Rig();
        rig.Probe.State = PendingNewer;
        var vm = Vm(rig);
        vm.SetRobloxFeedsEnabled(false);
        rig.Feeds.Start();
        vm.SetMainWindowVisible(true);

        await rig.RunAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1, rig.Probe.StateReads);
        Assert.Equal(0, rig.Api.Reads);
        Assert.Equal(RobloxUpdateKind.Pending, rig.Feeds.Update.Kind);
        Assert.NotNull(vm.RobloxLine.Top);
        Assert.True(vm.RobloxLine.Top!.ShowWhatChanged);
    }

    [Fact]
    public void WithNoCoordinatorAttached_TheSwitchStillClearsTheLine_AndNothingThrows()
    {
        var (vm, _, _, path) = MainViewModelTests.Build(uiDispatcher: new KnownIssuesNoticeModelTests.InlineDispatcher());
        _accountStores.Add(path);

        vm.SetRobloxFeedsEnabled(false);
        vm.SetRobloxFeedsEnabled(true);

        Assert.False(vm.RobloxLine.HasNotice);
    }
}
