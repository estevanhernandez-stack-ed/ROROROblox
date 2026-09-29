using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Spec rule 1 at batch scale: Squad Launch and Launch multiple hold <c>JoinViaFriend</c> accounts
/// for a joinable anchor and follow it. When no anchor lands, they ask ONCE for the whole batch
/// rather than joining the flagged accounts directly (the pre-2026-09 90 s fallback) or opening a
/// dialog per row.
/// </summary>
/// <remarks>
/// Every test awaits the batch itself, so nothing is still running when <c>finally</c> deletes the
/// temp store. Cleanup is best-effort, matching <see cref="FlaggedLaunchTests"/>.
/// </remarks>
public class FlaggedBatchLaunchTests
{
    /// <summary>
    /// A main that sits this batch out (deselected, offline). The store promotes the first account
    /// it saves to main, so without this AltA would BE the main, and a flagged main launches
    /// directly by design (review focus 1) instead of being held with the alts.
    /// </summary>
    private static async Task<AccountSummary> SeedIdleMainAsync(MainViewModel vm, IAccountStore store)
    {
        var main = new AccountSummary(await store.AddAsync("Main", "", "c0")) { RobloxUserId = 111, IsSelected = false };
        Assert.True(main.IsMain);
        vm.Accounts.Add(main);
        return main;
    }

    /// <summary>Two flagged alts. Seed a main first, or the store promotes AltA to main.</summary>
    private static async Task<(AccountSummary A, AccountSummary B)> SeedTwoFlaggedAsync(MainViewModel vm, IAccountStore store)
    {
        var addedA = await store.AddAsync("AltA", "", "c1");
        var addedB = await store.AddAsync("AltB", "", "c2");
        await store.SetJoinViaFriendAsync(addedA.Id, true);
        await store.SetJoinViaFriendAsync(addedB.Id, true);
        var a = new AccountSummary(addedA) { JoinViaFriend = true, RobloxUserId = 1 };
        var b = new AccountSummary(addedB) { JoinViaFriend = true, RobloxUserId = 2 };
        vm.Accounts.Add(a); vm.Accounts.Add(b);
        return (a, b);
    }

    private static void Quick(MainViewModel vm, TimeSpan anchorWait)
    {
        vm.InterLaunchThrottle = TimeSpan.Zero;
        vm.ServerVerificationPollInterval = TimeSpan.Zero;
        vm.ServerVerificationMaxWait = TimeSpan.Zero;
        vm.SquadServerResolveMaxWait = TimeSpan.Zero;
        vm.AnchorWait = anchorWait;
    }

    [Fact]
    public async Task SquadLaunch_NoAnchor_AsksOnceForAllFlaggedAccounts()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            await SeedIdleMainAsync(vm, store);
            await SeedTwoFlaggedAsync(vm, store);
            Quick(vm, TimeSpan.FromMilliseconds(50));
            var asks = new List<FlaggedLaunchAsk>();
            vm.FlaggedLaunchPrompt = ask => { asks.Add(ask); return new FlaggedLaunchChoice.Cancel(); };

            await vm.SquadLaunchAsync(new LaunchTarget.Place(5)).WaitAsync(TimeSpan.FromSeconds(10));

            var ask = Assert.Single(asks);
            Assert.Equal(["AltA", "AltB"], ask.AccountNames);
            Assert.Empty(launcher.Launches); // no silent direct join
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task SquadLaunch_NoAnchor_Cancel_LeavesThemStoppedAndSaysSo()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            await SeedIdleMainAsync(vm, store);
            await SeedTwoFlaggedAsync(vm, store);
            Quick(vm, TimeSpan.FromMilliseconds(50));
            vm.FlaggedLaunchPrompt = _ => new FlaggedLaunchChoice.Cancel();

            await vm.SquadLaunchAsync(new LaunchTarget.Place(5)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Empty(launcher.Launches);
            Assert.Equal("AltA, AltB left stopped: nobody they can follow is in a game.", vm.StatusBanner);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task SquadLaunch_NoAnchor_FollowChoice_SendsEveryFlaggedAccountAfterThatAccount()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            await SeedIdleMainAsync(vm, store);
            await SeedTwoFlaggedAsync(vm, store);
            Quick(vm, TimeSpan.FromMilliseconds(50));
            var asks = 0;
            vm.FlaggedLaunchPrompt = _ => { asks++; return new FlaggedLaunchChoice.FollowAccount(999); };

            await vm.SquadLaunchAsync(new LaunchTarget.Place(5)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(1, asks);
            Assert.Equal([new LaunchTarget.FollowFriend(999), new LaunchTarget.FollowFriend(999)], launcher.Launches);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task SquadLaunch_NoAnchor_JoinDirectly_ClearsEveryFlagThenJoinsTheTarget()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            await SeedIdleMainAsync(vm, store);
            var (a, b) = await SeedTwoFlaggedAsync(vm, store);
            Quick(vm, TimeSpan.FromMilliseconds(50));
            var asks = 0;
            vm.FlaggedLaunchPrompt = _ => { asks++; return new FlaggedLaunchChoice.JoinDirectly(); };
            var place = new LaunchTarget.Place(5);

            await vm.SquadLaunchAsync(place).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(1, asks);
            Assert.False(a.JoinViaFriend);
            Assert.False(b.JoinViaFriend);
            Assert.All(await store.ListAsync(), s => Assert.False(s.JoinViaFriend));
            Assert.Equal(2, launcher.Launches.Count);
            Assert.All(launcher.Launches, t => Assert.Same(place, t));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task SquadLaunch_DirectAnchorLands_FlaggedFollowItWithoutAsking()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var anchor = new AccountSummary(await store.AddAsync("Anchor", "", "c0")) { RobloxUserId = 77 };
            vm.Accounts.Add(anchor);
            await SeedTwoFlaggedAsync(vm, store);
            Quick(vm, TimeSpan.FromSeconds(5));
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("must not ask");
            var ps = new LaunchTarget.PrivateServer(5, "code", PrivateServerCodeKind.LinkCode);

            var squad = vm.SquadLaunchAsync(ps);
            // The direct batch has fired; now the anchor lands (only after launch, so it isn't
            // filtered out as already running before eligibility).
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (launcher.Launches.Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(10);
            vm.ApplyPresence(new AccountPresenceEventArgs(anchor.Id, UserPresenceType.InGame, 5, "Game",
                DateTimeOffset.UtcNow, new ServerInstance(5, "job-1")));
            await squad.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(3, launcher.Launches.Count);
            Assert.Same(ps, launcher.Launches[0]);
            Assert.Equal(new LaunchTarget.FollowFriend(77), launcher.Launches[1]);
            Assert.Equal(new LaunchTarget.FollowFriend(77), launcher.Launches[2]);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task LaunchAll_FlaggedRowsFollowTheMainOnceItLands()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (main, alt) = await FlaggedLaunchTests.SeedAsync(vm, store, mainInGame: true);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("must not ask");
            Quick(vm, TimeSpan.FromMilliseconds(200));

            await vm.LaunchAllForTestAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(new LaunchTarget.FollowFriend(111), Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task LaunchAll_TwoFlaggedAndNoJoinableMain_AsksOnceAndJoinsNobodyDirectly()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var main = new AccountSummary(await store.AddAsync("Main", "", "c0")) { IsMain = true, RobloxUserId = 111 };
            vm.Accounts.Add(main);
            await SeedTwoFlaggedAsync(vm, store);
            Quick(vm, TimeSpan.FromMilliseconds(50));
            var asks = new List<FlaggedLaunchAsk>();
            vm.FlaggedLaunchPrompt = ask => { asks.Add(ask); return new FlaggedLaunchChoice.Cancel(); };

            await vm.LaunchAllForTestAsync().WaitAsync(TimeSpan.FromSeconds(10));

            var ask = Assert.Single(asks);
            Assert.Equal(["AltA", "AltB"], ask.AccountNames);
            Assert.Equal("Main", ask.MainName);
            // The main launched (it's direct); it never landed, so neither alt went anywhere.
            Assert.IsType<LaunchTarget.DefaultGame>(Assert.Single(launcher.Launches));
            Assert.Equal("AltA, AltB left stopped: nobody they can follow is in a game.", vm.StatusBanner);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    // M3: the main sits this batch out and isn't running or launching, so it can't land in the
    // next 90 s. Ask straight away instead of holding the batch for AnchorWait.
    [Fact]
    public async Task LaunchAll_MainNotInTheBatchAndNotRunning_AsksAtOnce()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            await SeedIdleMainAsync(vm, store);
            await SeedTwoFlaggedAsync(vm, store);
            Quick(vm, TimeSpan.FromMinutes(10)); // were it to wait, the WaitAsync below would time out
            var asks = new List<FlaggedLaunchAsk>();
            vm.FlaggedLaunchPrompt = ask => { asks.Add(ask); return new FlaggedLaunchChoice.Cancel(); };

            await vm.LaunchAllForTestAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(["AltA", "AltB"], Assert.Single(asks).AccountNames);
            Assert.Empty(launcher.Launches);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task LaunchAll_MainNotInTheBatchButRunning_StillWaitsForIt()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, tracker, path) = MainViewModelTests.Build(launcher);
        try
        {
            var main = await SeedIdleMainAsync(vm, store);
            tracker.RaiseAttached(new RobloxProcessEventArgs(main.Id, 1111)); // running, not in a game yet
            await SeedTwoFlaggedAsync(vm, store);
            Quick(vm, TimeSpan.FromSeconds(5));
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("must not ask");

            var batch = vm.LaunchAllForTestAsync();
            vm.ApplyPresence(new AccountPresenceEventArgs(main.Id, UserPresenceType.InGame, 5, "Game",
                DateTimeOffset.UtcNow, new ServerInstance(5, "job-1")));
            await batch.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(2, launcher.Launches.Count);
            Assert.All(launcher.Launches, t => Assert.Equal(new LaunchTarget.FollowFriend(111), t));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task LaunchAll_FlaggedMain_LaunchesDirectlyAndNeverWaitsOnItself()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var added = await store.AddAsync("Main", "", "c0");
            await store.SetJoinViaFriendAsync(added.Id, true);
            vm.Accounts.Add(new AccountSummary(added) { IsMain = true, JoinViaFriend = true, RobloxUserId = 111 });
            Quick(vm, TimeSpan.FromMilliseconds(50));
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("must not ask");

            await vm.LaunchAllForTestAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.IsType<LaunchTarget.DefaultGame>(Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    /// <summary>Records every launch and runs a hook on the Nth, so a test can land a row in game at
    /// a known point in the batch rather than racing it.</summary>
    private sealed class HookLauncher(Action<int> onLaunch) : IRobloxLauncher
    {
        public List<LaunchTarget> Launches { get; } = [];

        public Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null)
        {
            Launches.Add(target);
            onLaunch(Launches.Count);
            return Task.FromResult<LaunchResult>(new LaunchResult.Started(7000 + Launches.Count, DateTimeOffset.UtcNow));
        }

        public Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null, int? fpsCap = null, long? browserTrackerId = null)
            => throw new NotImplementedException();
    }

    /// <summary>Main, an unflagged AltDirect and a flagged AltFollower, all selected and offline.
    /// Launch order in the batch: Main (#1), AltDirect (#2); AltFollower is held.</summary>
    private static async Task<(AccountSummary Main, AccountSummary Direct, AccountSummary Follower)> SeedMainDirectFollowerAsync(
        MainViewModel vm, IAccountStore store)
    {
        var main = new AccountSummary(await store.AddAsync("Main", "", "c0")) { RobloxUserId = 111 };
        Assert.True(main.IsMain);
        var direct = new AccountSummary(await store.AddAsync("AltDirect", "", "c1")) { RobloxUserId = 333 };
        var added = await store.AddAsync("AltFollower", "", "c2");
        await store.SetJoinViaFriendAsync(added.Id, true);
        var follower = new AccountSummary(added) { JoinViaFriend = true, RobloxUserId = 222 };
        vm.Accounts.Add(main); vm.Accounts.Add(direct); vm.Accounts.Add(follower);
        return (main, direct, follower);
    }

    [Fact]
    public async Task LaunchAll_AnUnflaggedAltLandsFirst_FlaggedRowNeverFollowsIt_AsksOnceWhenTheMainNeverLands()
    {
        AccountSummary? altDirect = null;
        // AltDirect is in game the moment it launches; the main's presence never moves.
        var launcher = new HookLauncher(n => { if (n == 2) altDirect!.PresenceState = UserPresenceType.InGame; });
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            (_, altDirect, _) = await SeedMainDirectFollowerAsync(vm, store);
            Quick(vm, TimeSpan.FromMilliseconds(300));
            var asks = new List<FlaggedLaunchAsk>();
            vm.FlaggedLaunchPrompt = ask => { asks.Add(ask); return new FlaggedLaunchChoice.Cancel(); };

            await vm.LaunchAllForTestAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(altDirect.InGame);
            Assert.DoesNotContain(new LaunchTarget.FollowFriend(333), launcher.Launches);
            var ask = Assert.Single(asks);
            Assert.Equal(["AltFollower"], ask.AccountNames);
            Assert.Equal(2, launcher.Launches.Count); // Main and AltDirect only
            Assert.All(launcher.Launches, t => Assert.IsType<LaunchTarget.DefaultGame>(t));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task LaunchAll_TheMainLands_FlaggedRowFollowsTheMainWithoutAsking()
    {
        AccountSummary? main = null;
        AccountSummary? altDirect = null;
        var launcher = new HookLauncher(n =>
        {
            if (n == 1) main!.PresenceState = UserPresenceType.InGame;
            if (n == 2) altDirect!.PresenceState = UserPresenceType.InGame;
        });
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            (main, altDirect, _) = await SeedMainDirectFollowerAsync(vm, store);
            Quick(vm, TimeSpan.FromSeconds(5));
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("must not ask");

            await vm.LaunchAllForTestAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(3, launcher.Launches.Count);
            Assert.Equal(new LaunchTarget.FollowFriend(111), launcher.Launches[2]);
            Assert.DoesNotContain(new LaunchTarget.FollowFriend(333), launcher.Launches);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }
}
