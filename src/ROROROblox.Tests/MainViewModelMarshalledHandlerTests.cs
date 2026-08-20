using ROROROblox.App.Notifications;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests;

/// <summary>
/// F-100. These exercise the five <c>MainViewModel</c> handler bodies that had never once executed
/// in a test, and they go through the real event handlers rather than the internal apply-methods —
/// the marshal is part of what is under test.
/// <para>
/// TWO OFF SWITCHES, IN SERIES. The register recorded one: every handler marshalled through
/// <c>Application.Current?.Dispatcher.Invoke(...)</c>, and <c>Application.Current</c> is null
/// across the whole ordinary suite, so the <c>?.</c> turned each call into a no-op and the delegate
/// inside never ran. Fixing that alone changes nothing, because there was a second: the doubles
/// declared the very events that drive those bodies as <c>{ add { } remove { } }</c>, throwing away
/// every subscription at bind time. The handlers were unreachable twice over, and the suite was
/// green both times — 1663 tests passing over five bodies that could not be entered.
/// </para>
/// <para>
/// Both are open now: the marshal goes through <see cref="IUiDispatcher"/>, whose shipped
/// implementation runs inline when there is no dispatcher, and the doubles raise real events.
/// </para>
/// </summary>
public class MainViewModelMarshalledHandlerTests
{
    /// <summary>Counts crossings so a test can prove the handler marshalled instead of calling straight through.</summary>
    private sealed class RecordingDispatcher : IUiDispatcher
    {
        public int Invocations;
        public void Invoke(Action action) { Invocations++; action(); }
    }

    private static AccountSummary Row(Guid id, string name = "alt") =>
        new(new Account(id, DisplayName: name, AvatarUrl: "", CreatedAt: DateTimeOffset.UtcNow,
                        LastLaunchedAt: null, FpsCap: null));

    [Fact]
    public void AClientAttachingMarksItsRowRunning()
    {
        var (vm, _, tracker, path) = MainViewModelTests.Build();
        try
        {
            var id = Guid.NewGuid();
            vm.Accounts.Add(Row(id));
            tracker.AttachedMap[id] = new TrackedProcess(4242, DateTimeOffset.UtcNow);

            tracker.RaiseAttached(new RobloxProcessEventArgs(id, 4242));

            var row = vm.Accounts.Single();
            Assert.True(row.IsRunning);
            Assert.Equal(4242, row.RunningPid);
            Assert.Equal(1, vm.LiveProcessCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AClientExitingClearsItsRow()
    {
        var (vm, _, tracker, path) = MainViewModelTests.Build();
        try
        {
            var id = Guid.NewGuid();
            vm.Accounts.Add(Row(id));
            tracker.AttachedMap[id] = new TrackedProcess(4242, DateTimeOffset.UtcNow);
            tracker.RaiseAttached(new RobloxProcessEventArgs(id, 4242));

            tracker.AttachedMap.Remove(id);
            tracker.RaiseExited(new RobloxProcessEventArgs(id, 4242));

            var row = vm.Accounts.Single();
            Assert.False(row.IsRunning);
            Assert.Null(row.RunningPid);
            Assert.Null(row.RunningSinceUtc);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnAttachThatNeverLandsSaysSoOnTheRow()
    {
        // The launcher fired and no client appeared. PreWarmGate owns the wording; what was never
        // covered is that the row receives it at all.
        var (vm, _, tracker, path) = MainViewModelTests.Build();
        try
        {
            var id = Guid.NewGuid();
            vm.Accounts.Add(Row(id));

            tracker.RaiseAttachFailed(new RobloxProcessEventArgs(id, 0));

            Assert.Equal(PreWarmGate.AttachFailedMessage(installerRunning: false), vm.Accounts.Single().StatusText);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AnIdleCrossingReachesTheTray()
    {
        var tray = new RecordingTray();
        var monitor = new MainViewModelTests.FakeActivityMonitor();
        var (vm, _, _, path) = MainViewModelTests.Build(tray: tray, activityMonitor: monitor);
        try
        {
            monitor.RaiseWarnCrossed([Guid.NewGuid()]);

            var toast = Assert.Single(tray.Toasts);
            Assert.Contains("idle", toast.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void AMemoryCrossingRaisesAnAlertNamingTheAccount()
    {
        // The crossing is edge-triggered and is the ONLY path that raises alerts — the 30s ticker
        // calls ApplyMemory without them. So this body is the alert system's entire front door and
        // nothing had ever opened it.
        var watchdog = new MainViewModelTests.FakeMemoryWatchdog();
        var (vm, _, _, path) = MainViewModelTests.Build(memoryWatchdog: watchdog);
        try
        {
            var id = Guid.NewGuid();
            vm.Accounts.Add(Row(id, "overcap-alt"));

            IReadOnlyList<AlertTrigger>? raised = null;
            vm.AlertsRaised += (_, triggers) => raised = triggers;

            watchdog.RaisePressureCrossed(new MemoryPressureSnapshot(
                AvailableBytes: 1_000, AggregateGrowthBytesPerHour: 0, MinutesToCeiling: 5,
                HasProjection: true, TargetAccountId: id,
                Accounts: [new AccountMemory(id, PrivateBytes: 9_000_000_000, GrowthBytesPerHour: 0,
                                             MinutesToCeiling: 5, OverCap: true, IsTarget: true, ReadOk: true)]));

            var trigger = Assert.Single(raised!);
            Assert.Equal(AlertKind.MemoryWarning, trigger.Kind);
            Assert.Equal(id, trigger.AccountId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TheHandlersMarshalRatherThanTouchingTheRowDirectly()
    {
        // The guard on the fix. These events arrive on threadpool threads — the presence poller
        // runs four at a time — and the rows are UI-bound, so a handler that stopped marshalling
        // would mutate a CollectionView from the wrong thread and leave the pickers half-rendered.
        // With the seam in place that regression is now a silent one-line deletion, so pin it.
        var dispatcher = new RecordingDispatcher();
        var (vm, _, tracker, path) = MainViewModelTests.Build(uiDispatcher: dispatcher);
        try
        {
            var id = Guid.NewGuid();
            vm.Accounts.Add(Row(id));
            var before = dispatcher.Invocations;

            tracker.RaiseAttached(new RobloxProcessEventArgs(id, 7));
            tracker.RaiseExited(new RobloxProcessEventArgs(id, 7));

            Assert.Equal(before + 2, dispatcher.Invocations);
        }
        finally { File.Delete(path); }
    }
}
