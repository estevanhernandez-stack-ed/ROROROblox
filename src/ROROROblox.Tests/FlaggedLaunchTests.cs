using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Spec rule 1 applied where every launch path meets (<c>MainViewModel.LaunchAccountAsync</c>): a
/// <c>JoinViaFriend</c> account follows the main when the main is joinable, and asks when it isn't.
/// It never joins directly on its own.
/// </summary>
/// <remarks>
/// Every <c>finally</c> block's temp-file cleanup below is best-effort
/// (<c>try</c>/<c>catch (IOException)</c>), matching the established convention already used
/// throughout this test project for scratch temp-file/dir cleanup (e.g. <c>BannerRecipeTests</c>,
/// <c>ContrastPairGateTests</c>, <c>ExpiredRowRedundancyTests</c>, <c>AppLoggingVersionTests</c>).
/// A scratch file's cleanup failing should never mask what the test above it already proved, and
/// the OS reclaims %TEMP% regardless.
/// <para>
/// <b>Fix round 2 correction.</b> Review round 1 attributed the full-suite-only flake on
/// <c>MainViewModelLaunchInvokerAdapterTests.RequestLaunch_FlaggedAltWithMainInGame_FollowsTheMain</c>
/// to real-time AV/EDR scanning transiently holding this file open — <b>that theory was wrong</b>.
/// The real cause was a fire-and-forget dispatch race in
/// <c>MainViewModelLaunchInvokerAdapter.RequestLaunchAsync</c> that let it return before the launch
/// had even set <c>IsLaunching = true</c>, which made <see cref="UntilSettledAsync"/> declare the
/// launch settled too early, this class's own cleanup delete the account-store file while a launch
/// was still actually queued, and the queued launch then hit <c>KeyNotFoundException</c> reading
/// the now-deleted file. Fixed at the dispatch site; see that method's fix-round-2 note. This
/// cleanup catch is kept because it independently matches this project's general convention, not
/// because of the AV/EDR theory.
/// </para>
/// </remarks>
public class FlaggedLaunchTests
{
    internal static async Task<(AccountSummary Main, AccountSummary Alt)> SeedAsync(MainViewModel vm, IAccountStore store, bool mainInGame)
    {
        var m = new AccountSummary(await store.AddAsync("Main", "", "c1")) { IsMain = true, RobloxUserId = 111 };
        var added = await store.AddAsync("Alt", "", "c2");
        await store.SetJoinViaFriendAsync(added.Id, true); // persisted, so "it's fixed" has something to clear
        var a = new AccountSummary(added) { JoinViaFriend = true, RobloxUserId = 222 };
        vm.Accounts.Add(m); vm.Accounts.Add(a);
        if (mainInGame)
            vm.ApplyPresence(new AccountPresenceEventArgs(m.Id, UserPresenceType.InGame, 5, "Game",
                DateTimeOffset.UtcNow, new ServerInstance(5, "job-1")));
        return (m, a);
    }

    /// <summary>
    /// The command is async void, so wait for the launch to finish rather than guessing a delay.
    /// <c>IsLaunching</c> goes true synchronously inside Execute and false in the launch's finally.
    /// </summary>
    internal static async Task UntilSettledAsync(AccountSummary row)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (row.IsLaunching && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.False(row.IsLaunching, "the launch never finished");
    }

    [Fact]
    public async Task FlaggedAlt_SingleLaunch_FollowsTheMain()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: true);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("must not ask");
            vm.LaunchAccountCommand.Execute(alt);
            await UntilSettledAsync(alt);
            Assert.Equal(new LaunchTarget.FollowFriend(111), Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task FlaggedAlt_MainNotInGame_AsksAndCancelLaunchesNothing()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: false);
            FlaggedLaunchAsk? asked = null;
            vm.FlaggedLaunchPrompt = ask => { asked = ask; return new FlaggedLaunchChoice.Cancel(); };
            vm.LaunchAccountCommand.Execute(alt);
            await UntilSettledAsync(alt);
            Assert.NotNull(asked);
            Assert.Equal(new[] { "Alt" }, asked!.AccountNames);
            Assert.Equal("Main", asked.MainName);
            Assert.Empty(launcher.Launches);
            Assert.False(alt.IsLaunching);
            Assert.Equal(string.Empty, alt.StatusText);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task FlaggedAlt_ItsFixed_ClearsTheFlagAndJoinsDirectly()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: false);
            vm.FlaggedLaunchPrompt = _ => new FlaggedLaunchChoice.JoinDirectly();
            vm.LaunchAccountCommand.Execute(alt);
            await UntilSettledAsync(alt);
            Assert.False(alt.JoinViaFriend);
            Assert.False((await store.ListAsync()).Single(a => a.Id == alt.Id).JoinViaFriend);
            Assert.IsType<LaunchTarget.DefaultGame>(Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task FlaggedAlt_ItsFixed_SaveFails_LaunchesNothingAndClearsTheLaunchingStatus()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(
            launcher, wrapStore: inner => new MainViewModelTests.JoinViaFriendThrowingStore(inner));
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: false); // Build hands back the inner store
            vm.FlaggedLaunchPrompt = _ => new FlaggedLaunchChoice.JoinDirectly();
            vm.LaunchAccountCommand.Execute(alt);
            await UntilSettledAsync(alt);
            Assert.Empty(launcher.Launches);
            Assert.True(alt.JoinViaFriend);
            Assert.Equal(string.Empty, alt.StatusText);
            Assert.False(alt.IsLaunching);
            Assert.Contains("Couldn't save join-via-friend", vm.StatusBanner);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task FlaggedAlt_FollowAnotherAccount_FollowsThePick()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: false);
            vm.FlaggedLaunchPrompt = _ => new FlaggedLaunchChoice.FollowAccount(333);
            vm.LaunchAccountCommand.Execute(alt);
            await UntilSettledAsync(alt);
            Assert.Equal(new LaunchTarget.FollowFriend(333), Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task FlaggedAlt_AskOffersOnlyJoinableOtherAccounts()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: false);
            var friend = new AccountSummary(await store.AddAsync("Friend", "", "c3")) { RobloxUserId = 333 };
            var idle = new AccountSummary(await store.AddAsync("Idle", "", "c4")) { RobloxUserId = 444 };
            vm.Accounts.Add(friend); vm.Accounts.Add(idle);
            vm.ApplyPresence(new AccountPresenceEventArgs(friend.Id, UserPresenceType.InGame, 7, "Game",
                DateTimeOffset.UtcNow, new ServerInstance(7, "job-7")));

            FlaggedLaunchAsk? asked = null;
            vm.FlaggedLaunchPrompt = ask => { asked = ask; return new FlaggedLaunchChoice.Cancel(); };
            vm.LaunchAccountCommand.Execute(alt);
            await UntilSettledAsync(alt);

            Assert.Equal(("Friend", 333L), Assert.Single(asked!.JoinableOthers));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task FlaggedAlt_JoinByLinkToAServerTheMainIsNotIn_Asks()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: true);
            var asked = false;
            vm.FlaggedLaunchPrompt = _ => { asked = true; return new FlaggedLaunchChoice.Cancel(); };
            vm.JoinByLinkPicker = _ => (new LaunchTarget.GameJob(5, "job-OTHER"), false);
            await vm.OpenJoinByLinkAsync(alt);
            Assert.True(asked);
            Assert.Empty(launcher.Launches);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task LaunchAccountForPluginAsync_JoinByLinkToAServerTheMainIsNotIn_RefusesAndDoesNotAsk()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: true);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("plugins never ask");
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.GameJob(5, "job-OTHER"));
            Assert.Empty(launcher.Launches);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task FlaggedAlt_PrivateServerLink_MainInThatPlaceButNotThatServer_RefusesAndDoesNotFollow()
    {
        // I5: the main is in place 5 (a public server, as far as its last launch says), and the
        // alt is handed the clan's private link for place 5. Place-only matching used to follow the
        // main into the public server.
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (main, alt) = await SeedAsync(vm, store, mainInGame: true);
            main.LastLaunchTarget = new LaunchTarget.Place(5);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("plugins never ask");
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.PrivateServer(5, "clan", PrivateServerCodeKind.LinkCode));
            Assert.Empty(launcher.Launches);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task FlaggedAlt_PrivateServerLink_MainInThatSamePrivateServer_FollowsTheMain()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (main, alt) = await SeedAsync(vm, store, mainInGame: true);
            main.LastLaunchTarget = new LaunchTarget.PrivateServer(5, "clan", PrivateServerCodeKind.LinkCode);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("plugins never ask");
            await vm.LaunchAccountForPluginAsync(alt, new LaunchTarget.PrivateServer(5, "clan", PrivateServerCodeKind.LinkCode));
            Assert.Equal(new LaunchTarget.FollowFriend(111), Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    // M2: Recycle stops the client before it relaunches. For a flagged account that would ask,
    // the question comes first, so Cancel leaves the client running.
    [Fact]
    public async Task Recycle_FlaggedAlt_MainNotJoinable_AsksBeforeStopping_AndCancelStopsNothing()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: false);
            alt.LastLaunchTarget = new LaunchTarget.Place(5);
            var asked = 0;
            vm.FlaggedLaunchPrompt = _ => { asked++; return new FlaggedLaunchChoice.Cancel(); };

            var ok = await vm.RecycleAccountAsync(alt).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(ok);
            Assert.Equal(1, asked);
            Assert.Empty(stopper.StoppedAccountIds); // the client is still running
            Assert.Empty(launcher.Launches);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task Recycle_FlaggedAlt_MainNotJoinable_FollowAnother_AsksOnceThenStopsAndFollows()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper,
            memoryWatchdog: new MainViewModelTests.SpyMemoryWatchdog());
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: false);
            alt.LastLaunchTarget = new LaunchTarget.Place(5);
            var asked = 0;
            var stoppedWhenAsked = -1;
            vm.FlaggedLaunchPrompt = _ =>
            {
                asked++;
                stoppedWhenAsked = stopper.StoppedAccountIds.Count;
                return new FlaggedLaunchChoice.FollowAccount(333);
            };

            var ok = await vm.RecycleAccountAsync(alt).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(ok);
            Assert.Equal(1, asked);            // not asked again after the stop
            Assert.Equal(0, stoppedWhenAsked); // asked while the client was still running
            Assert.Equal(alt.Id, Assert.Single(stopper.StoppedAccountIds));
            Assert.Equal(new LaunchTarget.FollowFriend(333), Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task Recycle_FlaggedAlt_MainInGame_FollowsWithoutAsking()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var stopper = new MainViewModelTests.FakeRobloxInstanceStopper();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher, instanceStopper: stopper,
            memoryWatchdog: new MainViewModelTests.SpyMemoryWatchdog());
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: true);
            alt.LastLaunchTarget = new LaunchTarget.Place(5);
            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("must not ask");

            var ok = await vm.RecycleAccountAsync(alt).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(ok);
            Assert.Equal(new LaunchTarget.FollowFriend(111), Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task FlaggedAlt_PrivateServerLink_MainStillLoadingThenInGame_StillFollows()
    {
        // I5 regression: ApplyPresence used to null LastLaunchTarget on EVERY not-in-game
        // presence reading, including the "still loading" one that lands while the main's own
        // PrivateServer launch is still starting up (PresenceService polls every 25s, and a
        // client's real first-InGame reading routinely lands after at least one such poll). That
        // wiped the proof FlaggedLaunchRule needs before the main ever reported InGame, so once
        // the main did land InGame in X, the alt's link asked (or refused) instead of following.
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (main, alt) = await SeedAsync(vm, store, mainInGame: false);
            var privateServer = new LaunchTarget.PrivateServer(5, "clan", PrivateServerCodeKind.LinkCode);
            await vm.LaunchAccountForPluginAsync(main, privateServer);
            Assert.Same(privateServer, main.LastLaunchTarget);

            // The main's client is still loading: presence answers not-in-game before its first
            // InGame poll ever lands. OnlineWebsite here (not Offline) is the closer real shape,
            // but the bug fired on either not-in-game reading.
            vm.ApplyPresence(new AccountPresenceEventArgs(
                main.Id, UserPresenceType.OnlineWebsite, null, null, DateTimeOffset.UtcNow));
            Assert.Same(privateServer, main.LastLaunchTarget); // the loading poll must not clear it

            // Now the client has actually joined the private server.
            vm.ApplyPresence(new AccountPresenceEventArgs(main.Id, UserPresenceType.InGame, 5, "Game",
                DateTimeOffset.UtcNow, new ServerInstance(5, "job-1")));

            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("plugins never ask");
            await vm.LaunchAccountForPluginAsync(alt, privateServer);

            Assert.Equal(new LaunchTarget.FollowFriend(111), launcher.Launches[1]);
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task FlaggedAlt_PrivateServerLink_MainReallyLeftThenInGameElsewhere_Refuses()
    {
        // The other half of the same fix: a GENUINE leave (InGame -> not-in-game, the client
        // actually exiting the private server) must still drop the stale credential, exactly as
        // the MINOR 1 comment in ApplyPresence describes -- otherwise a later public server of the
        // same place would wrongly inherit the old private-server proof.
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (main, alt) = await SeedAsync(vm, store, mainInGame: false);
            var privateServer = new LaunchTarget.PrivateServer(5, "clan", PrivateServerCodeKind.LinkCode);
            await vm.LaunchAccountForPluginAsync(main, privateServer);

            // Main actually joins the private server.
            vm.ApplyPresence(new AccountPresenceEventArgs(main.Id, UserPresenceType.InGame, 5, "Game",
                DateTimeOffset.UtcNow, new ServerInstance(5, "job-1")));
            Assert.Same(privateServer, main.LastLaunchTarget);

            // Main genuinely leaves (InGame -> not-in-game): the real leave the comment describes.
            vm.ApplyPresence(new AccountPresenceEventArgs(
                main.Id, UserPresenceType.Offline, null, null, DateTimeOffset.UtcNow));
            Assert.Null(main.LastLaunchTarget);

            // Main comes back InGame in the SAME place, but now a PUBLIC server (no matching
            // private code survives the leave).
            vm.ApplyPresence(new AccountPresenceEventArgs(main.Id, UserPresenceType.InGame, 5, "Game",
                DateTimeOffset.UtcNow, new ServerInstance(5, "job-PUBLIC")));

            vm.FlaggedLaunchPrompt = _ => throw new InvalidOperationException("plugins never ask");
            await vm.LaunchAccountForPluginAsync(alt, privateServer);

            Assert.Single(launcher.Launches); // only the main's launch; the alt refused
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public async Task UnflaggedAlt_IsUnchanged()
    {
        var launcher = new MainViewModelTests.RecordingSuccessLauncher();
        var (vm, store, _, path) = MainViewModelTests.Build(launcher);
        try
        {
            var (_, alt) = await SeedAsync(vm, store, mainInGame: true);
            alt.JoinViaFriend = false;
            vm.LaunchAccountCommand.Execute(alt);
            await UntilSettledAsync(alt);
            Assert.IsType<LaunchTarget.DefaultGame>(Assert.Single(launcher.Launches));
        }
        finally { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } }
    }
}
