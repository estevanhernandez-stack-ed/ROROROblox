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

    // ---- A superseded exit is the OLD client's, not the account's (v1.39 item 4) ----
    //
    // The account was launched again while its first client (pid 9001) was up; the tracker now holds
    // the second (pid 9002). When 9001 exits the tracker says Superseded and the row must not move.

    [Fact]
    public async Task ASupersededExit_LeavesTheLiveClientRunning_AndNothingClosesOrAlerts()
    {
        var history = new MainViewModelTests.RecordingSessionHistoryStore();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher: new MainViewModelTests.RecordingSuccessLauncher(), sessionHistory: history);
        try
        {
            var row = new AccountSummary(await store.AddAsync("Alt", "", "cookie")) { RobloxUserId = 12345 };
            vm.Accounts.Add(row);
            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(PlaceId: 8737899170));
            Assert.Single(history.Added);

            tracker.AttachedMap[row.Id] = new TrackedProcess(9001, DateTimeOffset.UtcNow);
            tracker.RaiseAttached(new RobloxProcessEventArgs(row.Id, 9001));
            vm.ApplyPresence(new AccountPresenceEventArgs(row.Id, UserPresenceType.InGame, 8737899170, "Pet Sim 99", DateTimeOffset.UtcNow));

            // The relaunch: the tracker's slot now holds 9002.
            tracker.AttachedMap[row.Id] = new TrackedProcess(9002, DateTimeOffset.UtcNow);
            tracker.RaiseAttached(new RobloxProcessEventArgs(row.Id, 9002));
            var runningSince = row.RunningSinceUtc;
            Assert.Equal(9002, row.RunningPid);

            var alerts = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => alerts.AddRange(triggers);

            tracker.RaiseExited(new RobloxProcessEventArgs(row.Id, 9001) { Superseded = true });

            Assert.True(row.IsRunning);
            Assert.Equal(9002, row.RunningPid);
            Assert.Equal(runningSince, row.RunningSinceUtc);
            Assert.Null(row.LastClosedAtUtc);
            Assert.Empty(history.Ended);

            // Presence reports the session over only once the live client is gone too. While it runs, the
            // both-signals rule cannot agree the row is closed, so no close is stamped and nobody is paged.
            vm.ApplyPresence(new AccountPresenceEventArgs(row.Id, UserPresenceType.Offline, null, null, DateTimeOffset.UtcNow));
            Assert.Null(row.LastClosedAtUtc);
            Assert.Empty(history.Ended);
            Assert.Empty(alerts);
            Assert.Equal(1, vm.LiveProcessCount);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task ASupersededExit_ForAnAccountPresenceCannotSee_StampsNoClose_ButTheRealExitStillDoes()
    {
        var history = new MainViewModelTests.RecordingSessionHistoryStore();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher: new MainViewModelTests.RecordingSuccessLauncher(), sessionHistory: history);
        try
        {
            var row = new AccountSummary(await store.AddAsync("Alt", "", "cookie")); // RobloxUserId null: the process is the only signal
            vm.Accounts.Add(row);
            await vm.LaunchAccountForPluginAsync(row, new LaunchTarget.Place(PlaceId: 8737899170));
            var session = Assert.Single(history.Added);

            tracker.AttachedMap[row.Id] = new TrackedProcess(9001, DateTimeOffset.UtcNow);
            tracker.RaiseAttached(new RobloxProcessEventArgs(row.Id, 9001));
            tracker.AttachedMap[row.Id] = new TrackedProcess(9002, DateTimeOffset.UtcNow);
            tracker.RaiseAttached(new RobloxProcessEventArgs(row.Id, 9002));

            tracker.RaiseExited(new RobloxProcessEventArgs(row.Id, 9001) { Superseded = true });

            Assert.True(row.IsRunning);
            Assert.Equal(9002, row.RunningPid);
            Assert.Null(row.LastClosedAtUtc);
            Assert.Empty(history.Ended);

            // The live client's own exit is a normal one and behaves exactly as before.
            var exitedAt = DateTimeOffset.UtcNow;
            tracker.AttachedMap.Remove(row.Id);
            tracker.RaiseExited(new RobloxProcessEventArgs(row.Id, 9002) { OccurredAtUtc = exitedAt });

            Assert.False(row.IsRunning);
            Assert.Null(row.RunningPid);
            Assert.Equal(exitedAt, row.LastClosedAtUtc);
            var ended = Assert.Single(history.Ended);
            Assert.Equal(session.Id, ended.SessionId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
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
    public void AnIdleCrossingRaisesAnAlertAndNoToastOfItsOwn()
    {
        // Was AnIdleCrossingReachesTheTray until 2026-10-06. v1.33 item 3 took the view model's
        // direct line to the tray away: the crossing raises an AlertKind.AccountIdle trigger and
        // AlertDispatcher decides whether anything is shown, so a toast arriving from HERE would
        // mean the third path to the screen came back. What this file is for is unchanged — the
        // marshalled handler body executes, proven through the real event rather than the internal
        // apply-method. IdleAlertTriggerTests holds the trigger's shape.
        var tray = new RecordingTray();
        var monitor = new MainViewModelTests.FakeActivityMonitor();
        var (vm, _, _, path) = MainViewModelTests.Build(tray: tray, activityMonitor: monitor);
        try
        {
            var id = Guid.NewGuid();
            vm.Accounts.Add(Row(id));

            // Idle means IN-GAME idle (Este's ruling 2026-10-07), so the row has to actually be in
            // a game or the crossing is correctly dropped and this test proves nothing.
            vm.ApplyPresence(new AccountPresenceEventArgs(
                id, UserPresenceType.InGame, placeId: 8737899170, gameName: "Pet Simulator 99!",
                occurredAtUtc: DateTimeOffset.UtcNow, server: null));

            IReadOnlyList<AlertTrigger>? raised = null;
            vm.AlertsRaised += (_, triggers) => raised = triggers;

            monitor.RaiseWarnCrossed([id]);

            Assert.NotNull(raised);
            Assert.Equal(AlertKind.AccountIdle, Assert.Single(raised!).Kind);
            Assert.Empty(tray.Toasts);
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
