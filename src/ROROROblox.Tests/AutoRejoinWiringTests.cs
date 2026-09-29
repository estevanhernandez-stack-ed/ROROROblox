using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Transport;

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
    public async Task FourthDropInAnHour_PausesWithAnAlert_AndTurningItBackOnResumes()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);
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
            var trigger = Assert.Single(raised);
            Assert.Equal(AlertKind.AutoRejoinPaused, trigger.Kind);
            Assert.Equal(AutoRejoinPauseReason.RepeatedDrops, trigger.PauseReason);
            Assert.Equal(alt.Id, trigger.AccountId);
            Assert.Equal("Alt", trigger.DisplayName);

            // I4: the pause is visible and durable: off on the row (the menu shows it off) and in
            // the store (a restart can't silently unpause it).
            Assert.False(alt.AutoRejoin);
            Assert.False((await store.ListAsync()).Single(a => a.Id == alt.Id).AutoRejoin);

            await vm.ToggleAutoRejoinAsync(alt).WaitAsync(Limit); // on again: resumes
            Assert.True(alt.AutoRejoin);
            Assert.True((await store.ListAsync()).Single(a => a.Id == alt.Id).AutoRejoin);

            // Resumed with a fresh clock (M1): the first pass starts it, the first-join grace later
            // it's due.
            await vm.RunAutoRejoinAsync(t0.AddMinutes(35)).WaitAsync(Limit);
            Assert.Equal(3, stopper.StoppedAccountIds.Count);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(40)).WaitAsync(Limit);
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
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            // Stands in for a throwing alert subscriber (a dead tray, say). RaiseAlerts's own guard
            // (not the per-action catch in RunAutoRejoinAsync) is what must stop this from stranding
            // B's Rejoin, due in the same batch right after A's Pause.
            var pauseAttempts = 0;
            vm.AlertsRaised += (_, triggers) =>
            {
                if (triggers.Any(t => t.Kind == AlertKind.AutoRejoinPaused))
                {
                    pauseAttempts++;
                    throw new InvalidOperationException("alert subscriber failed");
                }
            };
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

            Assert.Equal(1, pauseAttempts); // the alert was raised once, and its subscriber threw
            Assert.False(a.AutoRejoin);     // I4: paused shows as off
            Assert.True(b.AutoRejoin);
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
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);
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
            // Repeated drops, not a give-up: each cycle re-attached by hand before hitting the
            // pending-relaunch attempt cap, so it's the monitor's own 4th-drop Pause that fires.
            var trigger = Assert.Single(raised);
            Assert.Equal(AlertKind.AutoRejoinPaused, trigger.Kind);
            Assert.Equal(AutoRejoinPauseReason.RepeatedDrops, trigger.PauseReason);
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
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);
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
            Assert.Empty(raised); // never gave up: no AutoRejoinPaused alert
            Assert.Equal([alt.Id], stopper.StoppedAccountIds);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task PendingRelaunch_ThreeFailedAttempts_RaiseThePausedAlert_AndStop()
    {
        var launcher = new FailingLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);
            var (_, t0) = await StopThenFailRelaunchAsync(vm, store, tracker);

            for (var i = 0; i < MainViewModel.PendingRelaunchMaxAttempts; i++)
            {
                await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5 + 0.5 * i)).WaitAsync(Limit);
            }
            Assert.Equal(1 + MainViewModel.PendingRelaunchMaxAttempts, launcher.Calls);
            var trigger = Assert.Single(raised);
            Assert.Equal(AlertKind.AutoRejoinPaused, trigger.Kind);
            Assert.Equal(AutoRejoinPauseReason.RelaunchFailed, trigger.PauseReason);

            // Given up: no more attempts, no second alert.
            await vm.RunAutoRejoinAsync(t0.AddMinutes(7)).WaitAsync(Limit);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(7.5)).WaitAsync(Limit);
            Assert.Equal(1 + MainViewModel.PendingRelaunchMaxAttempts, launcher.Calls);
            Assert.Single(raised);
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
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);
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
            Assert.Empty(raised);

            // Fourth drop inside the hour: a Pause, not a fourth stop.
            await DropCycleAsync(vm, tracker, alt, t0.AddMinutes(30));
            Assert.Equal(3, stopper.StoppedAccountIds.Count);
            Assert.Equal(6, launcher.Calls);
            var trigger = Assert.Single(raised);
            Assert.Equal(AlertKind.AutoRejoinPaused, trigger.Kind);
            Assert.Equal(AutoRejoinPauseReason.RepeatedDrops, trigger.PauseReason);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task GivingUpOnAPendingRelaunch_PausesAutoRejoin_UntilItIsTurnedBackOn()
    {
        // The rejoin's launch and all three pending attempts fail; after that, launches start.
        var launcher = new FailingLauncher(failures: 1 + MainViewModel.PendingRelaunchMaxAttempts);
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);
            var (alt, t0) = await StopThenFailRelaunchAsync(vm, store, tracker);
            for (var i = 0; i < MainViewModel.PendingRelaunchMaxAttempts; i++)
            {
                await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5 + 0.5 * i)).WaitAsync(Limit);
            }
            var trigger = Assert.Single(raised); // given up
            Assert.Equal(AlertKind.AutoRejoinPaused, trigger.Kind);
            Assert.Equal(AutoRejoinPauseReason.RelaunchFailed, trigger.PauseReason);
            // I4: off on the row and in the store.
            Assert.False(alt.AutoRejoin);
            Assert.False((await store.ListAsync()).Single(a => a.Id == alt.Id).AutoRejoin);

            // The user launches it by hand; it drops out. Paused, so auto-rejoin leaves it be.
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.DefaultGame()).WaitAsync(Limit);
            await DropCycleAsync(vm, tracker, alt, t0.AddMinutes(10));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(20)).WaitAsync(Limit);
            Assert.Single(stopper.StoppedAccountIds);

            // Turned back on: the next due tick rejoins it.
            await vm.ToggleAutoRejoinAsync(alt).WaitAsync(Limit);
            Assert.True(alt.AutoRejoin);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(21)).WaitAsync(Limit); // fresh clock starts (M1)
            Assert.Single(stopper.StoppedAccountIds);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(26)).WaitAsync(Limit);
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

    // I1: after three presence 403s the row is SessionLimited and reads Offline. The client is
    // running and (as far as anyone knows) still in game: auto-rejoin must never kill it.
    [Fact]
    public async Task SessionLimitedRunningAlt_IsNeverStopped()
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
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);

            vm.ApplySessionLimited(alt.Id);
            Assert.False(alt.InGame);
            for (var i = 1; i <= 12; i++)
            {
                await vm.RunAutoRejoinAsync(t0.AddMinutes(i * 5)).WaitAsync(Limit);
            }

            Assert.Empty(stopper.StoppedAccountIds);
            Assert.Empty(launcher.Launches);
        }
        finally { Cleanup(path); }
    }

    // I1: a row with no Roblox user id is never polled, so it always reads Offline.
    [Fact]
    public async Task RowWithNoUserId_IsNeverStopped()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = null };
            vm.Accounts.Add(alt);
            vm.WaitForClientExitAsync = ExitsOnStop(tracker);
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.Place(5)).WaitAsync(Limit);
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            for (var i = 0; i <= 12; i++)
            {
                await vm.RunAutoRejoinAsync(t0.AddMinutes(i * 5)).WaitAsync(Limit);
            }

            Assert.Empty(stopper.StoppedAccountIds);
            Assert.Single(launcher.Launches); // only the user's own launch
        }
        finally { Cleanup(path); }
    }

    // I2 (a): in game A, closed by hand, launched by the user into game B, never reaches InGame.
    // The failed-join rejoin goes back to B, never to the server A presence once saw.
    [Fact]
    public async Task FailedJoinAfterTheUserLaunchedElsewhere_DoesNotTargetTheOldServer()
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
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.Place(5)).WaitAsync(Limit);
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0)); // game A: server (5, job-1)
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);

            // Closed by hand, then launched by the user into game B.
            tracker.RaiseExited(new RobloxProcessEventArgs(alt.Id, 4242));
            vm.ApplyPresence(P(alt.Id, false, t0));
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.Place(7)).WaitAsync(Limit);
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4343));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(1)).WaitAsync(Limit);

            await vm.RunAutoRejoinAsync(t0.AddMinutes(6)).WaitAsync(Limit); // the 5-min grace is up

            Assert.Equal([alt.Id], stopper.StoppedAccountIds);
            Assert.Equal(3, launcher.Launches.Count);
            Assert.Equal(new LaunchTarget.Place(7), launcher.Launches[2]);
        }
        finally { Cleanup(path); }
    }

    // I2 (b): the first rejoin goes to the exact server; when that join fails, the next one must
    // not keep aiming at the same (possibly dead) job id.
    [Fact]
    public async Task RepeatedFailedJoins_DoNotKeepRetargetingTheSameGameJob()
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
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.Place(5)).WaitAsync(Limit);
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
            var t0 = DateTimeOffset.UtcNow;
            vm.ApplyPresence(P(alt.Id, true, t0));
            await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
            vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit); // drop: exact server
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4343));  // never reaches InGame
            await vm.RunAutoRejoinAsync(t0.AddMinutes(10)).WaitAsync(Limit); // failed join
            tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4444));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(16)).WaitAsync(Limit); // failed join again

            Assert.Equal<LaunchTarget>(
                [new LaunchTarget.Place(5), new LaunchTarget.GameJob(5, "job-1"), new LaunchTarget.Place(5), new LaunchTarget.Place(5)],
                launcher.Launches);
        }
        finally { Cleanup(path); }
    }

    // I3: Stop all is the user saying "leave them stopped", including one auto-rejoin still owes.
    [Fact]
    public async Task PendingRelaunch_StopAll_ClearsIt()
    {
        var launcher = new FailingLauncher(failures: 1);
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var (_, t0) = await StopThenFailRelaunchAsync(vm, store, tracker);

            vm.ExpectCloseForAll();

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5)).WaitAsync(Limit);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(5)).WaitAsync(Limit);
            Assert.Equal(1, launcher.Calls); // only the rejoin's own failed launch
        }
        finally { Cleanup(path); }
    }

    // I3: a user launch that doesn't start (any outcome, not only Started) still settles the
    // pending entry: the user has taken the row over.
    [Fact]
    public async Task PendingRelaunch_AUserLaunchThatFails_StillClearsIt()
    {
        var launcher = new FailingLauncher(failures: 2);
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var (alt, t0) = await StopThenFailRelaunchAsync(vm, store, tracker);

            vm.LaunchAccountCommand.Execute(alt); // the user's own launch: failure two
            await FlaggedLaunchTests.UntilSettledAsync(alt);
            Assert.Equal(2, launcher.Calls);

            await vm.RunAutoRejoinAsync(t0.AddMinutes(4.5)).WaitAsync(Limit);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(5)).WaitAsync(Limit);
            Assert.Equal(2, launcher.Calls); // auto-rejoin didn't launch it again
        }
        finally { Cleanup(path); }
    }

    /// <summary>
    /// A main row (offline) plus an opted-in alt whose rejoin stopped the client and whose relaunch
    /// failed, so it is relaunch-pending. The alt is then flagged, as if the user ticked Join via
    /// friend afterwards.
    /// </summary>
    private static async Task<(AccountSummary Main, AccountSummary Alt, DateTimeOffset T0)> PendingThenFlaggedAsync(
        MainViewModel vm, IAccountStore store, MainViewModelTests.FakeRobloxProcessTracker tracker)
    {
        var main = new AccountSummary(await store.AddAsync("Main", "", "m")) { RobloxUserId = 1 };
        var added = await store.AddAsync("Alt", "", "c");
        var alt = new AccountSummary(added) { AutoRejoin = true, RobloxUserId = 2 };
        vm.Accounts.Add(main);
        vm.Accounts.Add(alt);
        vm.WaitForClientExitAsync = ExitsOnStop(tracker);
        tracker.RaiseAttached(new RobloxProcessEventArgs(alt.Id, 4242));
        var t0 = DateTimeOffset.UtcNow;
        vm.ApplyPresence(P(alt.Id, true, t0));
        await vm.RunAutoRejoinAsync(t0).WaitAsync(Limit);
        vm.ApplyPresence(P(alt.Id, false, t0.AddMinutes(1)));
        await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit); // stopped; relaunch fails
        Assert.False(alt.IsRunning);
        await store.SetJoinViaFriendAsync(added.Id, true);
        alt.JoinViaFriend = true;
        return (main, alt, t0);
    }

    // I3: the flagged-launch dialog's Cancel means "leave it stopped". A pending relaunch must
    // not bring it back once the main is joinable again.
    [Fact]
    public async Task PendingRelaunch_FlaggedDialogCancel_ClearsIt()
    {
        var launcher = new FailingLauncher(failures: 1);
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var (main, alt, t0) = await PendingThenFlaggedAsync(vm, store, tracker);
            var asked = 0;
            vm.FlaggedLaunchPrompt = _ => { asked++; return new FlaggedLaunchChoice.Cancel(); };

            vm.LaunchAccountCommand.Execute(alt); // main offline: asks; the user cancels
            await FlaggedLaunchTests.UntilSettledAsync(alt);
            Assert.Equal(1, asked);

            tracker.RaiseAttached(new RobloxProcessEventArgs(main.Id, 1111));
            vm.ApplyPresence(P(main.Id, true, t0.AddMinutes(5)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(5)).WaitAsync(Limit);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(5.5)).WaitAsync(Limit);
            Assert.Equal(1, launcher.Calls); // only the rejoin's own failed launch
        }
        finally { Cleanup(path); }
    }

    // I3: same for a batch. Launch multiple asks once for the flagged rows; Cancel leaves the
    // pending one stopped for good.
    [Fact]
    public async Task PendingRelaunch_BatchCancel_ClearsIt()
    {
        var launcher = new FailingLauncher(failures: 1);
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var (main, alt, t0) = await PendingThenFlaggedAsync(vm, store, tracker);
            main.IsSelected = false;
            alt.IsSelected = true;
            vm.AnchorWait = TimeSpan.FromMilliseconds(50);
            vm.InterLaunchThrottle = TimeSpan.Zero;
            var asked = 0;
            vm.FlaggedLaunchPrompt = _ => { asked++; return new FlaggedLaunchChoice.Cancel(); };

            await vm.LaunchAllForTestAsync().WaitAsync(Limit);
            Assert.Equal(1, asked);

            tracker.RaiseAttached(new RobloxProcessEventArgs(main.Id, 1111));
            vm.ApplyPresence(P(main.Id, true, t0.AddMinutes(5)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(5)).WaitAsync(Limit);
            await vm.RunAutoRejoinAsync(t0.AddMinutes(5.5)).WaitAsync(Limit);
            Assert.Equal(1, launcher.Calls);
        }
        finally { Cleanup(path); }
    }

    // I3 ruling: a flagged pending entry waiting for the main expires after 30 minutes, with a log
    // line and no alert.
    [Fact]
    public async Task FlaggedPendingRelaunch_WaitingForTheMain_ExpiresAfterThirtyMinutes()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi());
        try
        {
            var raised = new List<AlertTrigger>();
            vm.AlertsRaised += (_, triggers) => raised.AddRange(triggers);
            var main = new AccountSummary(await store.AddAsync("Main", "", "m")) { RobloxUserId = 1 };
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, JoinViaFriend = true, RobloxUserId = 2 };
            vm.Accounts.Add(main);
            vm.Accounts.Add(alt);
            var t0 = DateTimeOffset.UtcNow;
            tracker.RaiseAttached(new RobloxProcessEventArgs(main.Id, 1111));
            vm.ApplyPresence(P(main.Id, true, t0));
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
            await vm.RunAutoRejoinAsync(t0.AddMinutes(4)).WaitAsync(Limit); // stopped; main gone: pending
            Assert.Empty(launcher.Launches);

            // Waiting for the main, pass after pass, up to just under 30 minutes.
            for (var m = 5.0; m < 34; m += 5)
            {
                await vm.RunAutoRejoinAsync(t0.AddMinutes(m)).WaitAsync(Limit);
            }
            await vm.RunAutoRejoinAsync(t0.AddMinutes(34)).WaitAsync(Limit); // 30 min after the stop: expired

            vm.ApplyPresence(P(main.Id, true, t0.AddMinutes(35)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(35)).WaitAsync(Limit);
            Assert.Empty(launcher.Launches); // dropped, not relaunched once the main is back
            Assert.Empty(raised);             // and no alert
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public async Task FlaggedPendingRelaunch_MainBackInsideThirtyMinutes_StillRelaunches()
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

            vm.ApplyPresence(P(main.Id, true, t0.AddMinutes(33)));
            await vm.RunAutoRejoinAsync(t0.AddMinutes(33.5)).WaitAsync(Limit); // 29.5 min after the stop
            Assert.Equal(new LaunchTarget.FollowFriend(1), Assert.Single(launcher.Launches));
        }
        finally { Cleanup(path); }
    }

    // I4: the store refusing the write must not unpause anything. Logged; the monitor stays paused.
    [Fact]
    public async Task PausePersistFailure_KeepsTheMonitorPaused()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var failing = false;
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper, uiDispatcher: new InlineUi(),
            wrapStore: inner => new AutoRejoinWriteFailsStore(inner, () => failing));
        try
        {
            await SeedMainAsync(store);
            var alt = new AccountSummary(await store.AddAsync("Alt", "", "c")) { AutoRejoin = true, RobloxUserId = 2 };
            vm.Accounts.Add(alt);
            vm.WaitForClientExitAsync = ExitsOnStop(tracker);
            var t0 = DateTimeOffset.UtcNow;
            failing = true;
            for (var i = 0; i < 4; i++)
            {
                await DropCycleAsync(vm, tracker, alt, t0.AddMinutes(10 * i));
            }
            Assert.Equal(3, stopper.StoppedAccountIds.Count);

            // A later drop, still paused: nothing stopped.
            await DropCycleAsync(vm, tracker, alt, t0.AddMinutes(40));
            Assert.Equal(3, stopper.StoppedAccountIds.Count);
        }
        finally { Cleanup(path); }
    }

    private sealed class AutoRejoinWriteFailsStore(IAccountStore inner, Func<bool> failing) : IAccountStore
    {
        public Task SetAutoRejoinAsync(Guid id, bool autoRejoin)
            => failing() ? throw new IOException("disk full") : inner.SetAutoRejoinAsync(id, autoRejoin);
        public Task SetJoinViaFriendAsync(Guid id, bool joinViaFriend) => inner.SetJoinViaFriendAsync(id, joinViaFriend);
        public Task UpdateRobloxUserIdAsync(Guid accountId, long userId) => inner.UpdateRobloxUserIdAsync(accountId, userId);
        public Task UpdateBrowserTrackerIdAsync(Guid accountId, long browserTrackerId) => inner.UpdateBrowserTrackerIdAsync(accountId, browserTrackerId);
        public int GetCookieGeneration(Guid id) => inner.GetCookieGeneration(id);
        public Task<IReadOnlyList<Account>> ListAsync() => inner.ListAsync();
        public Task<Account> AddAsync(string displayName, string avatarUrl, string cookie) => inner.AddAsync(displayName, avatarUrl, cookie);
        public Task RemoveAsync(Guid id) => inner.RemoveAsync(id);
        public Task<string> RetrieveCookieAsync(Guid id) => inner.RetrieveCookieAsync(id);
        public Task UpdateCookieAsync(Guid id, string newCookie) => inner.UpdateCookieAsync(id, newCookie);
        public Task TouchLastLaunchedAsync(Guid id) => inner.TouchLastLaunchedAsync(id);
        public Task SetMainAsync(Guid id) => inner.SetMainAsync(id);
        public Task UpdateSortOrderAsync(IReadOnlyList<Guid> idsInOrder) => inner.UpdateSortOrderAsync(idsInOrder);
        public Task SetSelectedAsync(Guid id, bool isSelected) => inner.SetSelectedAsync(id, isSelected);
        public Task SetCaptionColorAsync(Guid id, string? hex) => inner.SetCaptionColorAsync(id, hex);
        public Task SetFpsCapAsync(Guid id, int? fps) => inner.SetFpsCapAsync(id, fps);
        public Task UpdateLocalNameAsync(Guid accountId, string? localName) => inner.UpdateLocalNameAsync(accountId, localName);
        public Task UpdateStreamerIdentityAsync(Guid accountId, string fakeName, string fakeAvatarId) => inner.UpdateStreamerIdentityAsync(accountId, fakeName, fakeAvatarId);
        public Task SetTagsAsync(Guid id, IReadOnlyList<string> tags) => inner.SetTagsAsync(id, tags);
        public Task<AccountExportResult> ExportAccountsAsync(IEnumerable<Guid> ids) => inner.ExportAccountsAsync(ids);
        public Task<ImportMergeResult> ImportMergeAsync(IReadOnlyList<AccountExportRecord> records) => inner.ImportMergeAsync(records);
    }
}
