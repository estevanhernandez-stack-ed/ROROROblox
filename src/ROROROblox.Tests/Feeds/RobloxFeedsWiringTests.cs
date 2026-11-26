using Microsoft.Extensions.DependencyInjection;
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
/// The feeds coordinator and the pulse service on the real composition root and the real
/// <see cref="MainViewModel"/> (v1.41 item 8). The coordinator and the view model are covered on their own
/// elsewhere; this is the seam between them, where a resolve-time crash or a missed assignment hides behind
/// a green unit run.
/// </summary>
public sealed class RobloxFeedsWiringTests : IDisposable
{
    private static readonly Version Latest = new(0, 748, 0, 7481526);
    private static readonly RobloxUpdateState Current = new(RobloxUpdateKind.Current, RobloxUpdateDirection.None, Latest);
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

        public bool Churn { get; set; }

        public int StateReads;

        public Task<RobloxUpdateState> GetUpdateStateAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref StateReads);
            return Task.FromResult(State);
        }

        public RobloxUpdateState CompareInstalledTo(Version latest) => State;

        public bool IsInstallerRunning() => false;

        public Task<bool> IsUpdatePendingAsync(CancellationToken ct = default) => Task.FromResult(State.Kind == RobloxUpdateKind.Pending);

        public bool IsUpdateChurnActive() => Churn;
    }

    private sealed class FakeStatusApi : IRobloxStatusApi
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public Task<RobloxStatusRead> ReadAsync(CancellationToken ct = default)
        {
            Interlocked.Increment(ref _reads);
            var snapshot = new RobloxStatusSnapshot(
                RobloxStatusWatch.Roblox.Select(w => new StatusCell(w.ComponentId, w.ContainerId, 100)).ToList());
            return Task.FromResult(new RobloxStatusRead(FeedOutcome.Ok, snapshot));
        }
    }

    private sealed class NoStrap : IBloxstrapDetector
    {
        public bool IsBloxstrapHandler() => false;

        public bool IsStrapHandlingLaunches() => false;
    }

    private sealed class RecordingPulse : IGamePulseService
    {
        public DateTimeOffset? Allowed;

        public int AllowCalls;

        public event EventHandler<GamePulseChangedEventArgs>? Changed { add { } remove { } }

        public GamePulseView? TryGet(long universeId) => null;

        public void SetInterest(object owner, IReadOnlyCollection<long> universeIds) { }

        public void SetEnabled(bool enabled) { }

        public void AllowReadsFrom(DateTimeOffset instant)
        {
            Allowed = instant;
            AllowCalls++;
        }
    }

    private sealed class ScriptedLauncher(LaunchResult result) : IRobloxLauncher
    {
        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
            => Task.FromResult(result);

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
    }

    private sealed class Rig : IDisposable
    {
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 26, 12, 0, 0, TimeSpan.Zero));

        public FakeProbe Probe { get; } = new();

        public FakeStatusApi Api { get; } = new();

        public RobloxFeeds Feeds { get; }

        public Rig() => Feeds = new RobloxFeeds(() => Probe, () => Api, new NoStrap(), Clock, NullLogger<RobloxFeeds>.Instance);

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

        public void Dispose() => Feeds.Dispose();
    }

    private MainViewModel Vm(IRobloxLauncher? launcher = null, IRobloxUpdateProbe? probe = null)
        => VmWithStore(launcher, probe).Vm;

    private (MainViewModel Vm, IAccountStore Store) VmWithStore(IRobloxLauncher? launcher = null, IRobloxUpdateProbe? probe = null)
    {
        var (vm, store, _, path) = MainViewModelTests.Build(
            launcher: launcher,
            bloxstrapDetector: new NoStrap(),
            uiDispatcher: new KnownIssuesNoticeModelTests.InlineDispatcher(),
            updateProbe: probe);
        _accountStores.Add(path);
        return (vm, store);
    }

    [Fact]
    public void ProductionDi_ResolvesTheFeedsAndThePulseService_AndConstructionAsksNothing()
    {
        // A resolve-time crash (a missing registration, a ctor the container cannot fill) is invisible to a
        // unit run and would end the app at startup. The REAL composition root builds both.
        var services = new ServiceCollection();
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);
        using var provider = services.BuildServiceProvider();

        var feeds = provider.GetRequiredService<RobloxFeeds>();
        var pulse = provider.GetRequiredService<GamePulseService>();

        Assert.Same(feeds, provider.GetRequiredService<RobloxFeeds>());
        Assert.Same(pulse, provider.GetRequiredService<IGamePulseService>());
        Assert.Equal(RobloxUpdateKind.Unknown, feeds.Update.Kind);
        Assert.Null(feeds.ReadsAllowedFrom);
        Assert.Null(pulse.TryGet(1));

        feeds.Dispose();
        pulse.Dispose();
    }

    [Fact]
    public void TheCompositionRoot_HandsBothToTheViewModel_StartsAndDisposesThem_AndTheWindowPushesVisibility()
    {
        // The wiring is a property assignment, and a property nobody assigns leaves the feeds silently dead
        // with every test green. Source assertions on the places the spec names (section 5.3).
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.NotNull(root);
        string Code(params string[] parts)
        {
            var path = Path.Combine(new[] { root!, "src", "ROROROblox.App" }.Concat(parts).ToArray());
            return string.Join('\n', SettingsReachabilityTests.WithoutComments(path, File.ReadAllLines(path)));
        }

        var app = Code("App.xaml.cs");
        Assert.Contains("vm.RobloxFeeds = feeds;", app, StringComparison.Ordinal);
        Assert.Contains("vm.GamePulse = _services.GetRequiredService<ROROROblox.Core.Library.IGamePulseService>();", app, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<RobloxFeeds>().Start();", app, StringComparison.Ordinal);
        Assert.Contains("ProcessAttached += (_, _) => feeds.NoteClientAttached();", app, StringComparison.Ordinal);
        Assert.Contains("GetService<RobloxFeeds>()?.Dispose();", app, StringComparison.Ordinal);
        Assert.Contains("GetService<ROROROblox.Core.Library.GamePulseService>()?.Dispose();", app, StringComparison.Ordinal);

        var window = Code("MainWindow.xaml.cs");
        Assert.Contains("SetMainWindowVisible(IsVisible && WindowState != WindowState.Minimized)", window, StringComparison.Ordinal);
        Assert.Contains("IsVisibleChanged +=", window, StringComparison.Ordinal);
        Assert.Contains("StateChanged +=", window, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBatchGate_OnACurrentClient_LaunchesAllNow_AndTheCoordinatorHoldsWhatItSaw()
    {
        using var rig = new Rig();
        var probe = new FakeProbe { State = Current };
        var vm = Vm(probe: probe);
        vm.RobloxFeeds = rig.Feeds;

        var decision = await vm.DecidePreWarmAsync();

        Assert.Equal(PreWarmDecision.LaunchAllNow, decision);
        Assert.Equal(1, probe.StateReads);
        Assert.Equal(Current, rig.Feeds.Update);
        Assert.False(vm.RobloxUpdating);
    }

    [Fact]
    public async Task TheBatchGate_WithAnUpdatePending_HoldsAsInV17_AndTheCoordinatorHoldsWhatItSaw()
    {
        using var rig = new Rig();
        var probe = new FakeProbe { State = PendingNewer };
        var vm = Vm(probe: probe);
        vm.RobloxFeeds = rig.Feeds;

        var decision = await vm.DecidePreWarmAsync();

        Assert.Equal(PreWarmDecision.PreWarmThenRelease, decision);
        Assert.Equal(PendingNewer, rig.Feeds.Update);
    }

    [Fact]
    public async Task TheBatchGate_WorksWithNoCoordinatorAtAll()
    {
        var probe = new FakeProbe { State = PendingNewer };
        var vm = Vm(probe: probe);

        Assert.Equal(PreWarmDecision.PreWarmThenRelease, await vm.DecidePreWarmAsync());
    }

    [Theory]
    [InlineData(LaunchFailureKind.NoConnection, true)]
    [InlineData(LaunchFailureKind.TimedOut, true)]
    [InlineData(LaunchFailureKind.RobloxUnavailable, true)]
    [InlineData(LaunchFailureKind.RateLimited, false)]
    [InlineData(LaunchFailureKind.AuthTicketFailed, false)]
    [InlineData(LaunchFailureKind.ProcessStartFailed, false)]
    [InlineData(LaunchFailureKind.NoDefaultGame, false)]
    public async Task AFailedLaunch_RequestsAnEarlyStatusRead_ForTheThreeConnectionKindsOnly(LaunchFailureKind kind, bool requested)
    {
        using var rig = new Rig();
        var (vm, store) = VmWithStore(new ScriptedLauncher(new LaunchResult.Failed(kind, "test")));
        vm.RobloxFeeds = rig.Feeds;
        vm.SetMainWindowVisible(true);
        rig.Feeds.Start();

        await rig.RunAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(1, rig.Api.Reads);                      // the scheduled first read
        await rig.RunAsync(TimeSpan.FromSeconds(120));
        Assert.Equal(1, rig.Api.Reads);                      // past the one-minute spacing, well short of ten

        var row = new AccountSummary(await store.AddAsync("TestAlt", "", "cookie"))
        {
            SessionNeedsVerification = true, // routes the health action to a plain launch of Roblox home
        };
        await vm.RunHealthActionAsync(row);                  // nothing awaits a feed: this returns on its own
        await rig.RunAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(requested ? 2 : 1, rig.Api.Reads);
    }

    [Fact]
    public async Task TheWindowsVisibility_ReachesTheCoordinator_AndOpensThePulseGate()
    {
        using var rig = new Rig();
        var pulse = new RecordingPulse();
        var vm = Vm();
        vm.RobloxFeeds = rig.Feeds;
        vm.GamePulse = pulse;
        rig.Feeds.Start();

        Assert.Null(pulse.Allowed); // never shown: the pulse asks nothing

        var shown = rig.Clock.GetUtcNow();
        vm.SetMainWindowVisible(true);
        Assert.Equal(shown + FeedOptions.Default.SettleAfterShown, rig.Feeds.ReadsAllowedFrom);
        Assert.Equal(rig.Feeds.ReadsAllowedFrom, pulse.Allowed);

        vm.SetMainWindowVisible(false);
        vm.SetMainWindowVisible(true);
        Assert.Equal(1, pulse.AllowCalls); // the same instant is not pushed twice

        await rig.RunAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(1, rig.Api.Reads);

        vm.SetMainWindowVisible(false); // the tray, or minimised
        await rig.RunAsync(TimeSpan.FromMinutes(12));
        Assert.Equal(1, rig.Api.Reads);
        Assert.Equal(1, rig.Probe.StateReads);
    }

    [Fact]
    public void TheCoordinatorsChange_MovesTheLine_AndStopPeriodicRefreshLetsGo()
    {
        using var rig = new Rig();
        var vm = Vm();
        vm.RobloxFeeds = rig.Feeds;

        rig.Feeds.ReportUpdateState(PendingNewer);
        Assert.True(vm.RobloxLine.HasNotice);

        // The hard rule for a test that builds a view model: no handler outlives it.
        vm.StopPeriodicRefresh();
        rig.Feeds.ReportUpdateState(RobloxUpdateState.Unknown);

        Assert.True(vm.RobloxLine.HasNotice);
    }

    [Fact]
    public void ReplacingTheCoordinator_DetachesTheOldOne()
    {
        using var first = new Rig();
        using var second = new Rig();
        var vm = Vm();
        vm.RobloxFeeds = first.Feeds;
        vm.RobloxFeeds = second.Feeds;

        first.Feeds.ReportUpdateState(PendingNewer);

        Assert.False(vm.RobloxLine.HasNotice);
    }
}
