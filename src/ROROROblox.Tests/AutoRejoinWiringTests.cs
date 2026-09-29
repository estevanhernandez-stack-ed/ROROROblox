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

    /// <summary>
    /// The first <see cref="Failures"/> launches fail outright (pid 0); after that they start.
    /// Defaults to failing forever.
    /// </summary>
    private sealed class FailingLauncher(int failures = int.MaxValue) : IRobloxLauncher
    {
        public int Failures { get; } = failures;
        /// <summary>When set, decides per call number (1-based) instead of <see cref="Failures"/>.</summary>
        public Func<int, bool>? FailWhen { get; init; }
        public int Calls;
        public readonly List<LaunchTarget> Launches = [];
        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
        {
            Calls++;
            Launches.Add(target);
            return Task.FromResult<LaunchResult>((FailWhen?.Invoke(Calls) ?? Calls <= Failures)
                ? new LaunchResult.Failed(LaunchFailureKind.ProcessStartFailed, "test")
                : new LaunchResult.Started(9000 + Calls, DateTimeOffset.UtcNow));
        }

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Every await here that could park (a pass, a wait seam, a TaskCompletionSource) is bounded by
    /// this, so a bug fails the test instead of hanging the test host.
    /// </summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

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
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.Place(5)).WaitAsync(Limit);
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(1)).WaitAsync(Limit);
            Assert.Empty(stopper.StoppedAccountIds);

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit);

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
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            tracker.RaiseExited(new RobloxProcessEventArgs(alt.Id, 4242));
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(10)).WaitAsync(Limit);
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
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit); // no main at all: must not throw
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
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(a.Id, false, t0.AddMinutes(1)));

            // A is due; its pass parks on the client-exit wait with B still in game.
            var first = vm.RunAutoRejoinAsync(t0.AddMinutes(4));
            Assert.False(first.IsCompleted);
            Assert.Equal([a.Id], stopper.StoppedAccountIds);

            // B drops too and is due by t0+10, but a pass is still running: this one does nothing.
            vm.ApplyPresence(P(b.Id, false, t0.AddMinutes(5)));
            var second = vm.RunAutoRejoinAsync(t0.AddMinutes(10));
            Assert.True(second.IsCompleted);
            await second.WaitAsync(Limit);
            Assert.Equal([a.Id], stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches);

            tracker.RaiseExited(new RobloxProcessEventArgs(a.Id, 4242));
            aExited.SetResult();
            await first.WaitAsync(Limit);
            Assert.Single(launcher.Launches);

            // B wasn't lost, only deferred: the next pass picks it up.
            await vm.RunAutoRejoinAsync(t0.AddMinutes(11)).WaitAsync(Limit);
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
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit);
            Assert.Single(stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches); // never a second client alongside the first

            // Skipped, not spent: the next tick (past the stop's grace window) is free to try again.
            await vm.RunAutoRejoinAsync(t0.AddMinutes(6)).WaitAsync(Limit);
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
                await vm.RunAutoRejoinAsync(b).WaitAsync(Limit);
                vm.ApplyPresence(P(alt.Id, false, b.AddMinutes(1)));
                await vm.RunAutoRejoinAsync(b.AddMinutes(4)).WaitAsync(Limit);
            }

            Assert.Equal(3, stopper.StoppedAccountIds.Count);
            Assert.Equal(3, launcher.Launches.Count);
            var toast = Assert.Single(tray.Toasts);
            Assert.Equal("Auto-rejoin paused", toast.Title);
            Assert.Equal("Alt dropped out 4 times in an hour. Auto-rejoin is paused for it.", toast.Message);

            await vm.ToggleAutoRejoinAsync(alt).WaitAsync(Limit); // off
            await vm.ToggleAutoRejoinAsync(alt).WaitAsync(Limit); // on again: resumes
            Assert.True(alt.AutoRejoin);

            await vm.RunAutoRejoinAsync(t0.AddMinutes(35)).WaitAsync(Limit);
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
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(main.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(10)).WaitAsync(Limit);
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
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.DefaultGame()).WaitAsync(Limit);
            Assert.Equal(new LaunchTarget.FollowFriend(1), Assert.Single(launcher.Launches));

            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(main.Id, false, t0.AddMinutes(1)));
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            // Due from t0+4 on, with the main offline: seven refused ticks. Were a refusal to spend
            // budget, the fourth would already have paused it.
            for (var i = 0; i < 7; i++)
            {
                await vm.RunAutoRejoinAsync(t0.AddMinutes(4 + 0.5 * i)).WaitAsync(Limit);
            }
            Assert.Empty(stopper.StoppedAccountIds);
            Assert.Single(launcher.Launches);

            // The main is back: the next tick rejoins by following it.
            vm.ApplyPresence(P(main.Id, true, t0.AddMinutes(7.5)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(8)).WaitAsync(Limit);
            Assert.Equal([alt.Id], stopper.StoppedAccountIds);
            Assert.Equal(2, launcher.Launches.Count);
            Assert.Equal(new LaunchTarget.FollowFriend(1), launcher.Launches[1]);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task FlaggedAlt_Rejoins_ByFollowingTheMain()
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
            vm.WaitForClientExitAsync = ExitsOnStop(tracker);
            var t0 = DateTimeOffset.UtcNow;

            // Main is in game.
            tracker.RaiseAttached(new RobloxProcessEventArgs(main.Id, 1111));
            vm.ApplyPresence(P(main.Id, true, t0));

            // Alt is launched normally while the main is in game: stored target is FollowFriend(main).
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.DefaultGame()).WaitAsync(Limit);
            Assert.Equal(new LaunchTarget.FollowFriend(1), Assert.Single(launcher.Launches));

            // Alt is in game.
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);

            // Alt drops out.
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            // At 4 minutes, the alt is past the 3-minute threshold and auto-rejoin triggers.
            // It is stopped once and relaunched once with FollowFriend(main).
            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit);

            Assert.Equal([alt.Id], stopper.StoppedAccountIds);
            Assert.Equal(2, launcher.Launches.Count);
            Assert.Equal(new LaunchTarget.FollowFriend(1), launcher.Launches[1]);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task FlaggedAlt_MainLeavesDuringTheExitWait_IsRelaunchedOnceTheMainIsBack()
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
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit);

            Assert.Equal([alt.Id], stopper.StoppedAccountIds); // the main was joinable when it stopped
            Assert.Empty(launcher.Launches);                   // but not by launch time

            // Relaunch pending. While the main is away, passes wait and spend no attempts: more
            // passes than the attempt budget, and still no launch and no give-up.
            for (var i = 0; i < MainViewModel.PendingRelaunchMaxAttempts + 2; i++)
            {
                await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5 + 0.5 * i)).WaitAsync(Limit);
            }
            Assert.Empty(launcher.Launches);

            // The main is back: the next pass relaunches the (closed) alt by following it.
            vm.ApplyPresence(P(main.Id, true, t0.AddMinutes(8)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(8.5)).WaitAsync(Limit);
            Assert.Equal(new LaunchTarget.FollowFriend(1), Assert.Single(launcher.Launches));
            Assert.Equal([alt.Id], stopper.StoppedAccountIds);

            // Settled: a later pass doesn't launch it again.
            await vm.RunAutoRejoinAsync(t0.AddMinutes(9)).WaitAsync(Limit);
            Assert.Single(launcher.Launches);
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
                await vm.RunAutoRejoinAsync(at).WaitAsync(Limit);
                vm.ApplyPresence(P(a.Id, false, at.AddMinutes(1)));
                await vm.RunAutoRejoinAsync(at.AddMinutes(4)).WaitAsync(Limit);
            }
            Assert.Equal([a.Id, a.Id, a.Id], stopper.StoppedAccountIds);

            // Fourth drop for A (a Pause, whose toast throws) and B's first, due in the same tick.
            var t3 = t0.AddMinutes(30);
            tracker.RaiseAttached(new RobloxProcessEventArgs(a.Id, 4242));
            vm.ApplyPresence(P(a.Id, true, t3));
            vm.ApplyPresence(P(b.Id, true, t3));
            await vm.RunAutoRejoinAsync(t3).WaitAsync(Limit);
            vm.ApplyPresence(P(a.Id, false, t3.AddMinutes(1)));
            vm.ApplyPresence(P(b.Id, false, t3.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t3.AddMinutes(4)).WaitAsync(Limit);

            Assert.Single(tray.Toasts); // the pause toast was attempted, and threw
            Assert.Equal([a.Id, a.Id, a.Id, b.Id], stopper.StoppedAccountIds);
            Assert.Equal(4, launcher.Launches.Count);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task ARelaunchThatFails_StillSpendsTheSlot_SoTheFourthDropPauses()
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

            // Four drops inside the hour, each relaunch failing (pid 0). The client WAS stopped each
            // time, so each cycle counts: three stops, then the fourth drop pauses instead.
            // (The client is re-attached each cycle, as if the user had started it by hand, which
            // also drops the relaunch-pending entry.)
            for (var i = 0; i < 4; i++)
            {
                var at = t0.AddMinutes(10 * i);
                tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
                vm.ApplyPresence(P(alt.Id, true, at));
                await vm.RunAutoRejoinAsync(at).WaitAsync(Limit);
                vm.ApplyPresence(P(alt.Id, false, at.AddMinutes(1)));
                await vm.RunAutoRejoinAsync(at.AddMinutes(4)).WaitAsync(Limit);
            }

            Assert.Equal(3, stopper.StoppedAccountIds.Count);
            Assert.Equal(3, launcher.Calls);
            Assert.Equal("Auto-rejoin paused", Assert.Single(tray.Toasts).Title);
        }
        finally { Cleanup(path); }
    }

    /// <summary>
    /// Drives one alt to a stopped-but-not-relaunched state: in game at t0, out at t0+1, due at
    /// t0+4, stopped, and the relaunch fails (the launcher's first failure).
    /// </summary>
    private static async Task<(AccountSummary Alt, DateTimeOffset T0)> StopThenFailRelaunchAsync(
        MainViewModel vm, IAccountStore store, MainViewModelTests.FakeRobloxProcessTracker tracker)
    {
        await SeedMainAsync(store);
        var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
        vm.Accounts.Add(alt);
        vm.WaitForClientExitAsync = ExitsOnStop(tracker);
        tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
        var t0 = DateTimeOffset.UtcNow;
        vm.ApplyPresence(P(alt.Id, true, t0));
        await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
        vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
        await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit);
        Assert.False(alt.IsRunning);
        return (alt, t0);
    }

    [Fact]
    public async Task PendingRelaunch_FailsTwice_ThenStartsOnTheThirdPass()
    {
        var launcher = new FailingLauncher(failures: 2);
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var tray = new RecordingTray();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, tray: tray, uiDispatcher: new InlineUi());
        try
        {
            var (alt, t0) = await StopThenFailRelaunchAsync(vm, store, tracker);
            Assert.Equal(1, launcher.Calls); // the rejoin's own launch: failure one

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5)).WaitAsync(Limit);
            Assert.Equal(2, launcher.Calls); // pending retry: failure two

            await vm.RunAutoRejoinAsync(t0.AddMinutes(5)).WaitAsync(Limit);
            Assert.Equal(3, launcher.Calls); // pending retry: starts

            // Same target every time: the one worked out before the stop.
            Assert.All(launcher.Launches, t => Assert.Equal(launcher.Launches[0], t));

            await vm.RunAutoRejoinAsync(t0.AddMinutes(5.5)).WaitAsync(Limit);
            Assert.Equal(3, launcher.Calls); // settled
            Assert.Empty(tray.Toasts);
            Assert.Equal([alt.Id], stopper.StoppedAccountIds);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task PendingRelaunch_ThreeFailedAttempts_RaiseThePausedAlert_AndStop()
    {
        var launcher = new FailingLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var tray = new RecordingTray();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, tray: tray, uiDispatcher: new InlineUi());
        try
        {
            var (_, t0) = await StopThenFailRelaunchAsync(vm, store, tracker);

            for (var i = 0; i < MainViewModel.PendingRelaunchMaxAttempts; i++)
            {
                await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5 + 0.5 * i)).WaitAsync(Limit);
            }
            Assert.Equal(1 + MainViewModel.PendingRelaunchMaxAttempts, launcher.Calls);
            var toast = Assert.Single(tray.Toasts);
            Assert.Equal("Auto-rejoin paused", toast.Title);

            // Given up: no more attempts, no second alert.
            await vm.RunAutoRejoinAsync(t0.AddMinutes(7)).WaitAsync(Limit);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(7.5)).WaitAsync(Limit);
            Assert.Equal(1 + MainViewModel.PendingRelaunchMaxAttempts, launcher.Calls);
            Assert.Single(tray.Toasts);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task PendingRelaunch_TurningAutoRejoinOff_ClearsIt()
    {
        var launcher = new FailingLauncher(failures: 1);
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var (alt, t0) = await StopThenFailRelaunchAsync(vm, store, tracker);

            // Off and straight back on before any pass runs. Were the entry only skipped while
            // off (not cleared), this next pass would relaunch the closed client.
            await vm.ToggleAutoRejoinAsync(alt).WaitAsync(Limit);
            await vm.ToggleAutoRejoinAsync(alt).WaitAsync(Limit);
            Assert.True(alt.AutoRejoin);

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5)).WaitAsync(Limit);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(5)).WaitAsync(Limit);
            Assert.Equal(1, launcher.Calls); // only the rejoin's own failed launch
        }
        finally { Cleanup(path); }
    }

    /// <summary>One drop cycle for an unflagged alt: attach, in game at b, out at b+1, due at b+4.</summary>
    private static async Task DropCycleAsync(MainViewModel vm, MainViewModelTests.FakeRobloxProcessTracker tracker,
        AccountSummary alt, DateTimeOffset b)
    {
        tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
        vm.ApplyPresence(P(alt.Id, true, b));
        await vm.RunAutoRejoinAsync(b).WaitAsync(Limit);
        vm.ApplyPresence(P(alt.Id, false, b.AddMinutes(1)));
        await vm.RunAutoRejoinAsync(b.AddMinutes(4)).WaitAsync(Limit);
    }

    [Fact]
    public async Task PendingRelaunches_StillCountAgainstTheHourlyBudget()
    {
        // Every rejoin's own launch fails; the relaunch-pending retry on the next pass succeeds.
        var launcher = new FailingLauncher { FailWhen = call => call % 2 == 1 };
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

            for (var i = 0; i < 3; i++)
            {
                var b = t0.AddMinutes(10 * i);
                await DropCycleAsync(vm, tracker, alt, b);                       // stop, launch fails
                await vm.RunAutoRejoinAsync(b.AddMinutes(4.5)).WaitAsync(Limit); // pending retry starts
            }
            Assert.Equal(3, stopper.StoppedAccountIds.Count);
            Assert.Equal(6, launcher.Calls);
            Assert.Empty(tray.Toasts);

            // Fourth drop inside the hour: a Pause, not a fourth stop.
            await DropCycleAsync(vm, tracker, alt, t0.AddMinutes(30));
            Assert.Equal(3, stopper.StoppedAccountIds.Count);
            Assert.Equal(6, launcher.Calls);
            Assert.Equal("Auto-rejoin paused", Assert.Single(tray.Toasts).Title);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task GivingUpOnAPendingRelaunch_PausesAutoRejoin_UntilItIsTurnedBackOn()
    {
        // The rejoin's launch and all three pending attempts fail; after that, launches start.
        var launcher = new FailingLauncher(failures: 1 + MainViewModel.PendingRelaunchMaxAttempts);
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var tray = new RecordingTray();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, tray: tray, uiDispatcher: new InlineUi());
        try
        {
            var (alt, t0) = await StopThenFailRelaunchAsync(vm, store, tracker);
            for (var i = 0; i < MainViewModel.PendingRelaunchMaxAttempts; i++)
            {
                await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5 + 0.5 * i)).WaitAsync(Limit);
            }
            Assert.Single(tray.Toasts); // given up

            // The user launches it by hand; it drops out. Paused, so auto-rejoin leaves it be.
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.DefaultGame()).WaitAsync(Limit);
            await DropCycleAsync(vm, tracker, alt, t0.AddMinutes(10));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(20)).WaitAsync(Limit);
            Assert.Single(stopper.StoppedAccountIds);

            // Turned back on: the next due tick rejoins it.
            await vm.ToggleAutoRejoinAsync(alt).WaitAsync(Limit);
            await vm.ToggleAutoRejoinAsync(alt).WaitAsync(Limit);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(21)).WaitAsync(Limit);
            Assert.Equal(2, stopper.StoppedAccountIds.Count);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task AUserStopDuringTheExitWait_CancelsTheRelaunch()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            // While auto-rejoin waits for the exit, the user presses Stop on the same account.
            vm.WaitForClientExitAsync = id =>
            {
                vm.ExpectClose(id);
                tracker.RaiseExited(new RobloxProcessEventArgs(id, 4242));
                return Task.CompletedTask;
            };
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5)).WaitAsync(Limit); // no relaunch-pending either
            await vm.RunAutoRejoinAsync(t0.AddMinutes(10)).WaitAsync(Limit);

            Assert.Single(stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches);
        }
        finally { Cleanup(path); }
    }

    /// <summary>
    /// Task 9: a plugin stop is never mistaken for a drop. <c>ProcessTrackerAccountStopper.OnStopping</c>
    /// calls exactly <c>vm.ExpectClose(id)</c> — the same stamp a user's own Stop button leaves — so
    /// this test drives it the same way: directly through <c>ExpectClose</c>, standing in for the
    /// plugin's hook, rather than through a live stopper.
    /// </summary>
    [Fact]
    public async Task PluginStop_OfAnOptedInAlt_IsNotRelaunched()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(3.9)).WaitAsync(Limit); // not due yet

            vm.ExpectClose(alt.Id); // what the plugin stopper's OnStopping hook does
            await vm.RunAutoRejoinAsync(DateTimeOffset.UtcNow.AddSeconds(5)).WaitAsync(Limit); // inside the 60 s window

            Assert.Empty(launcher.Launches);
        }
        finally { Cleanup(path); }
    }

    /// <summary>
    /// Same shape as <see cref="AUserStopDuringTheExitWait_CancelsTheRelaunch"/>, because
    /// <c>ExpectClose</c> does not know or care who called it — a plugin's stop gets exactly the
    /// same cancellation of the auto-rejoin already in flight for the same account.
    /// </summary>
    [Fact]
    public async Task APluginStopDuringTheExitWait_CancelsTheRelaunch()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            // While auto-rejoin waits for the exit, a plugin stops the same account -- the path is
            // ProcessTrackerAccountStopper.OnStopping -> vm.UiDispatcher.Invoke(() => vm.ExpectClose(id)).
            vm.WaitForClientExitAsync = id =>
            {
                vm.ExpectClose(id);
                tracker.RaiseExited(new RobloxProcessEventArgs(id, 4242));
                return Task.CompletedTask;
            };
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5)).WaitAsync(Limit); // no relaunch-pending either
            await vm.RunAutoRejoinAsync(t0.AddMinutes(10)).WaitAsync(Limit);

            Assert.Single(stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task TurningAutoRejoinOffDuringTheExitWait_CancelsTheRelaunch()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            vm.WaitForClientExitAsync = id =>
            {
                tracker.RaiseExited(new RobloxProcessEventArgs(id, 4242));
                return vm.ToggleAutoRejoinAsync(alt); // off, while the client exits
            };
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit);
            Assert.False(alt.AutoRejoin);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5)).WaitAsync(Limit);

            Assert.Single(stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches);
        }
        finally { Cleanup(path); }
    }
}
