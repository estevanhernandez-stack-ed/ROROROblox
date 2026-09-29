using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Spec rule 1 applied where every launch path meets (<c>MainViewModel.LaunchAccountAsync</c>): a
/// <c>JoinViaFriend</c> account follows the main when the main is joinable, and asks when it isn't.
/// It never joins directly on its own.
/// </summary>
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
        finally { if (File.Exists(path)) File.Delete(path); }
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
        finally { if (File.Exists(path)) File.Delete(path); }
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
        finally { if (File.Exists(path)) File.Delete(path); }
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
        finally { if (File.Exists(path)) File.Delete(path); }
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
        finally { if (File.Exists(path)) File.Delete(path); }
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
        finally { if (File.Exists(path)) File.Delete(path); }
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
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
