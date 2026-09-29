using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Task 7: the <see cref="AutoRejoinMonitor"/> wired into <see cref="MainViewModel"/>. Every test
/// awaits <see cref="MainViewModel.RunAutoRejoinAsync"/> directly (Build stops the 30 s ticker), runs
/// the UI marshal inline so nothing hops to the suite's shared WPF App thread, and replaces the
/// 15 s client-exit poll with a seam, so there are no real waits anywhere.
/// </summary>
public class AutoRejoinWiringTests
{
    /// <summary>Runs every marshalled action inline, on the calling (test) thread.</summary>
    private sealed class InlineUi : IUiDispatcher
    {
        public void Invoke(Action action) => action();
    }

    private sealed class RecordingTray : ITrayService
    {
        public readonly List<(string Title, string Message)> Toasts = [];
        public bool ThrowOnToast;
        public void ShowToast(string title, string message)
        {
            Toasts.Add((title, message));
            if (ThrowOnToast) throw new InvalidOperationException("toast failed");
        }
        public void Show() { }
        public void UpdateStatus(MultiInstanceState state) { }
        public void Dispose() { }
        public void SetMemoryWarning(bool active) { }
        public void ShowMemoryWarning(string title, string message, Guid accountId) { }
        public event EventHandler<MultiInstanceState>? StatusChanged { add { } remove { } }
        public event EventHandler? RequestOpenMainWindow { add { } remove { } }
        public event EventHandler? RequestToggleMutex { add { } remove { } }
        public event EventHandler? RequestStopAllInstances { add { } remove { } }
        public event EventHandler? RequestQuit { add { } remove { } }
        public event EventHandler? RequestOpenDiagnostics { add { } remove { } }
        public event EventHandler? RequestOpenLogs { add { } remove { } }
        public event EventHandler? RequestOpenPreferences { add { } remove { } }
        public event EventHandler? RequestOpenHistory { add { } remove { } }
        public event EventHandler? RequestOpenPlugins { add { } remove { } }
        public event EventHandler? RequestActivateMain { add { } remove { } }
        public event EventHandler<Guid>? RequestFocusAccount { add { } remove { } }
    }

    /// <summary>Every launch fails outright, so the relaunch returns pid 0.</summary>
    private sealed class FailingLauncher : IRobloxLauncher
    {
        public int Calls;
        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
        {
            Calls++;
            return Task.FromResult<LaunchResult>(new LaunchResult.Failed(LaunchFailureKind.ProcessStartFailed, "test"));
        }

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
    }

    private static AccountPresenceEventArgs P(Guid id, bool inGame, DateTimeOffset at) =>
        inGame ? new(id, UserPresenceType.InGame, 5, "Game", at, new ServerInstance(5, "job-1"))
               : new(id, UserPresenceType.Offline, null, null, at);

    /// <summary>
    /// The store promotes the FIRST account it saves to main, and the main is never rejoined, so
    /// every alt-shaped test saves a main first. It stays out of <c>vm.Accounts</c>: as far as the
    /// view model can see there is no main row, so a flagged alt has nothing to follow.
    /// </summary>
    private static Task SeedMainAsync(IAccountStore store) => store.AddAsync("Main", "", "m");

    private static void Cleanup(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    /// <summary>The seam a well-behaved client takes: the stop lands and the process exits.</summary>
    private static Func<Guid, Task> ExitsOnStop(MainViewModelTests.FakeRobloxProcessTracker tracker) => id =>
    {
        tracker.RaiseExited(new RobloxProcessEventArgs(id, 4242));
        return Task.CompletedTask;
    };

    [Fact]
    public async Task OptedInAlt_OutOfGameThreeMinutes_IsStoppedAndRelaunchedIntoItsServer()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            vm.WaitForClientExitAsync = ExitsOnStop(tracker);
            // Launched once through the VM into a plain Place, so the stored target is Place(5):
            // auto-rejoin targets as Recycle does, and Upgrade pins that to the server presence saw.
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.Place(5));
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(1));
            Assert.Empty(stopper.StoppedAccountIds);

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4));

            Assert.Equal(alt.Id, Assert.Single(stopper.StoppedAccountIds));
            Assert.Equal<LaunchTarget>([new LaunchTarget.Place(5), new LaunchTarget.GameJob(5, "job-1")], launcher.Launches);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ClosedClient_IsNotRelaunched()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            tracker.RaiseExited(new RobloxProcessEventArgs(alt.Id, 4242));
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(10));
            Assert.Empty(stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task NoPromptEverOpens()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, JoinViaFriend = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("auto-rejoin never asks");
            vm.WaitForClientExitAsync = _ => Task.CompletedTask;
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)); // no main at all: must not throw
            Assert.Empty(launcher.Launches);
            // The flagged pre-check refuses BEFORE anything is stopped (ruling: stop nothing).
            Assert.Empty(stopper.StoppedAccountIds);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ASecondTick_WhileARejoinIsStillWaiting_IsANoOp()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var a = new AccountSummary(await store.AddAsync("AltA", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            var b = new AccountSummary(await store.AddAsync("AltB", "", "c")) { AutoRejoin = true, RobloxUserId = 3 };
            vm.Accounts.Add(a);
            vm.Accounts.Add(b);
            // A's client takes its time exiting; B's exits at once.
            var aExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            vm.WaitForClientExitAsync = id =>
            {
                if (id == a.Id) return aExited.Task;
                tracker.RaiseExited(new RobloxProcessEventArgs(id, 4343));
                return Task.CompletedTask;
            };
            tracker.RaiseAttached(new RobloxProcessEventArgs(a.Id, 4242));
            tracker.RaiseAttached(new RobloxProcessEventArgs(b.Id, 4343));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(a.Id, true, t0));
            vm.ApplyPresence(P(b.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            vm.ApplyPresence(P(a.Id, false, t0.AddMinutes(1)));

            // A is due; its pass parks on the client-exit wait with B still in game.
            var first = vm.RunAutoRejoinAsync(t0.AddMinutes(4));
            Assert.False(first.IsCompleted);
            Assert.Equal([a.Id], stopper.StoppedAccountIds);

            // B drops too and is due by t0+10, but a pass is still running: this one does nothing.
            vm.ApplyPresence(P(b.Id, false, t0.AddMinutes(5)));
            var second = vm.RunAutoRejoinAsync(t0.AddMinutes(10));
            Assert.True(second.IsCompleted);
            await second;
            Assert.Equal([a.Id], stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches);

            tracker.RaiseExited(new RobloxProcessEventArgs(a.Id, 4242));
            aExited.SetResult();
            await first;
            Assert.Single(launcher.Launches);

            // B wasn't lost, only deferred: the next pass picks it up.
            await vm.RunAutoRejoinAsync(t0.AddMinutes(11));
            Assert.Equal([a.Id, b.Id], stopper.StoppedAccountIds);
            Assert.Equal(2, launcher.Launches.Count);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ClientThatNeverExits_IsNotRelaunched_AndTheNextTickDecidesAgain()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            vm.WaitForClientExitAsync = _ => Task.CompletedTask; // the wait "timed out": IsRunning is still true
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4));
            Assert.Single(stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches); // never a second client alongside the first

            // Skipped, not spent: the next tick (past the stop's grace window) is free to try again.
            await vm.RunAutoRejoinAsync(t0.AddMinutes(6));
            Assert.Equal(2, stopper.StoppedAccountIds.Count);
            Assert.Empty(launcher.Launches);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task FourthDropInAnHour_PausesWithAToast_AndTurningItBackOnResumes()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var tray = new RecordingTray();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, tray: tray, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            vm.WaitForClientExitAsync = ExitsOnStop(tracker);
            var t0 = DateTimeOffset.UtcNow;

            for (var i = 0; i < 4; i++)
            {
                var b = t0.AddMinutes(10 * i);
                tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
                vm.ApplyPresence(P(alt.Id, true, b));
                await vm.RunAutoRejoinAsync(b);
                vm.ApplyPresence(P(alt.Id, false, b.AddMinutes(1)));
                await vm.RunAutoRejoinAsync(b.AddMinutes(4));
            }

            Assert.Equal(3, stopper.StoppedAccountIds.Count);
            Assert.Equal(3, launcher.Launches.Count);
            var toast = Assert.Single(tray.Toasts);
            Assert.Equal("Auto-rejoin paused", toast.Title);
            Assert.Equal("Alt dropped out 4 times in an hour. Auto-rejoin is paused for it.", toast.Message);

            await vm.ToggleAutoRejoinAsync(alt); // off
            await vm.ToggleAutoRejoinAsync(alt); // on again: resumes
            Assert.True(alt.AutoRejoin);

            await vm.RunAutoRejoinAsync(t0.AddMinutes(35));
            Assert.Equal(4, stopper.StoppedAccountIds.Count);
            Assert.Equal(4, launcher.Launches.Count);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task TheMain_IsNeverRejoined()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            // AutoRejoin forced on the row directly: the toggle refuses the main, so this is the
            // belt to that brace.
            var main = new AccountSummary(await store.AddAsync("Main", "", "c")) { AutoRejoin = true, IsMain = true, RobloxUserId = 1 };
            vm.Accounts.Add(main);
            vm.WaitForClientExitAsync = ExitsOnStop(tracker);
            tracker.RaiseAttached(new RobloxProcessEventArgs(main.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(main.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            vm.ApplyPresence(P(main.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(10));
            Assert.Empty(stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task FlaggedAlt_LastLaunchedByFollowing_MainOffline_StopsNothingAndSpendsNoBudget()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var main = new AccountSummary(await store.AddAsync("Main", "", "m")) { RobloxUserId = 1 };
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, JoinViaFriend = true, RobloxUserId = 2 };
            vm.Accounts.Add(main);
            vm.Accounts.Add(alt);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("auto-rejoin never asks");
            vm.WaitForClientExitAsync = ExitsOnStop(tracker);
            var t0 = DateTimeOffset.UtcNow;
            tracker.RaiseAttached(new RobloxProcessEventArgs(main.Id, 1111));
            vm.ApplyPresence(P(main.Id, true, t0));

            // Launched normally while the main was in game: the stored target is FollowFriend(main).
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.DefaultGame());
            Assert.Equal(new LaunchTarget.FollowFriend(1), Assert.Single(launcher.Launches));

            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            vm.ApplyPresence(P(main.Id, false, t0.AddMinutes(1)));
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            // Due from t0+4 on, with the main offline: seven refused ticks. Were a refusal to spend
            // budget, the fourth would already have paused it.
            for (var i = 0; i < 7; i++)
            {
                await vm.RunAutoRejoinAsync(t0.AddMinutes(4 + 0.5 * i));
            }
            Assert.Empty(stopper.StoppedAccountIds);
            Assert.Single(launcher.Launches);

            // The main is back: the next tick rejoins by following it.
            vm.ApplyPresence(P(main.Id, true, t0.AddMinutes(7.5)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(8));
            Assert.Equal([alt.Id], stopper.StoppedAccountIds);
            Assert.Equal(2, launcher.Launches.Count);
            Assert.Equal(new LaunchTarget.FollowFriend(1), launcher.Launches[1]);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task FlaggedAlt_MainLeavesDuringTheExitWait_IsNotLaunched()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var main = new AccountSummary(await store.AddAsync("Main", "", "m")) { RobloxUserId = 1 };
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, JoinViaFriend = true, RobloxUserId = 2 };
            vm.Accounts.Add(main);
            vm.Accounts.Add(alt);
            var t0 = DateTimeOffset.UtcNow;
            tracker.RaiseAttached(new RobloxProcessEventArgs(main.Id, 1111));
            vm.ApplyPresence(P(main.Id, true, t0));
            // While the stopped client exits, the main drops out of its game.
            vm.WaitForClientExitAsync = id =>
            {
                vm.ApplyPresence(P(main.Id, false, t0.AddMinutes(4)));
                tracker.RaiseExited(new RobloxProcessEventArgs(id, 4242));
                return Task.CompletedTask;
            };
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4));

            Assert.Equal([alt.Id], stopper.StoppedAccountIds); // the main was joinable when it stopped
            Assert.Empty(launcher.Launches);                   // but not by launch time
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task APauseThatThrows_DoesNotStrandARejoinInTheSameTick()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var tray = new RecordingTray { ThrowOnToast = true };
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, tray: tray, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var a = new AccountSummary(await store.AddAsync("AltA", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            var b = new AccountSummary(await store.AddAsync("AltB", "", "c")) { AutoRejoin = true, RobloxUserId = 3 };
            vm.Accounts.Add(a); // A first, so its Pause comes before B's Rejoin in the batch
            vm.Accounts.Add(b);
            vm.WaitForClientExitAsync = ExitsOnStop(tracker);
            var t0 = DateTimeOffset.UtcNow;
            tracker.RaiseAttached(new RobloxProcessEventArgs(b.Id, 4343));
            vm.ApplyPresence(P(b.Id, true, t0));

            // A drops three times and is rejoined each time; B stays in game throughout.
            for (var i = 0; i < 3; i++)
            {
                var at = t0.AddMinutes(10 * i);
                tracker.RaiseAttached(new RobloxProcessEventArgs(a.Id, 4242));
                vm.ApplyPresence(P(a.Id, true, at));
                await vm.RunAutoRejoinAsync(at);
                vm.ApplyPresence(P(a.Id, false, at.AddMinutes(1)));
                await vm.RunAutoRejoinAsync(at.AddMinutes(4));
            }
            Assert.Equal([a.Id, a.Id, a.Id], stopper.StoppedAccountIds);

            // Fourth drop for A (a Pause, whose toast throws) and B's first, due in the same tick.
            var t3 = t0.AddMinutes(30);
            tracker.RaiseAttached(new RobloxProcessEventArgs(a.Id, 4242));
            vm.ApplyPresence(P(a.Id, true, t3));
            vm.ApplyPresence(P(b.Id, true, t3));
            await vm.RunAutoRejoinAsync(t3);
            vm.ApplyPresence(P(a.Id, false, t3.AddMinutes(1)));
            vm.ApplyPresence(P(b.Id, false, t3.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t3.AddMinutes(4));

            Assert.Single(tray.Toasts); // the pause toast was attempted, and threw
            Assert.Equal([a.Id, a.Id, a.Id, b.Id], stopper.StoppedAccountIds);
            Assert.Equal(4, launcher.Launches.Count);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ARelaunchThatFails_IsRefunded_SoLaterDropsRejoinWithoutAPause()
    {
        var launcher = new FailingLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var tray = new RecordingTray();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, tray: tray, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            vm.WaitForClientExitAsync = ExitsOnStop(tracker);
            var t0 = DateTimeOffset.UtcNow;

            // Four drops inside the hour, each relaunch failing (pid 0). Refunded every time, so
            // none of them spends budget: no pause, and each due tick stops-and-relaunches again.
            // (The client is re-attached each cycle, as if the user had started it by hand.)
            for (var i = 0; i < 4; i++)
            {
                var at = t0.AddMinutes(10 * i);
                tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
                vm.ApplyPresence(P(alt.Id, true, at));
                await vm.RunAutoRejoinAsync(at);
                vm.ApplyPresence(P(alt.Id, false, at.AddMinutes(1)));
                await vm.RunAutoRejoinAsync(at.AddMinutes(4));
            }

            Assert.Equal(4, stopper.StoppedAccountIds.Count);
            Assert.Equal(4, launcher.Calls);
            Assert.Empty(tray.Toasts);
        }
        finally { Cleanup(path); }
    }
}
