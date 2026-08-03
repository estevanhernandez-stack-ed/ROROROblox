using ROROROblox.Core;

namespace ROROROblox.Tests.Discord;

public class JoinDispatchTests
{
    [Fact]
    public async Task HandleDiscordJoinAsync_PrivateServer_WarnsBeforeLaunching()
    {
        // Este's call: private servers are joinable, and the joiner is told they may bounce.
        // Roblox does the permission check server-side, so a mystery failure is the alternative.
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        string? shown = null;
        var target = new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode);

        await vm.HandleDiscordJoinAsync(target, msg => { shown = msg; return true; });

        Assert.NotNull(shown);
        Assert.Contains("denied entry", shown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleDiscordJoinAsync_PublicServer_LaunchesWithoutAWarning()
    {
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        var confirmed = false;

        await vm.HandleDiscordJoinAsync(
            new LaunchTarget.GameJob(140403681187145, "job-a"), _ => { confirmed = true; return true; });

        Assert.False(confirmed);
    }

    [Fact]
    public async Task HandleDiscordJoinAsync_UserDeclinesTheWarning_LaunchesNothing()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var target = new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode);

        var ok = await vm.HandleDiscordJoinAsync(target, _ => false);

        Assert.False(ok);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task HandleDiscordJoinAsync_NoAccountsConfigured_IsAnEmptyStateNotAnError()
    {
        var (vm, _) = DiscordTestHarness.VmWithNoAccounts();

        var ok = await vm.HandleDiscordJoinAsync(new LaunchTarget.GameJob(1, "j"), _ => true);

        Assert.False(ok);   // nothing to launch, and no exception
    }

    // Fix round 1 (Finding 2): when nothing is idle, the fallback CAN land on an already-running
    // account -- Roblox enforces one session per account server-side, so this takes over (kicks)
    // that account's live session. Not a plan change (the plan's selection order is unchanged) --
    // this only asserts the takeover isn't SILENT: the join still launches, but StatusBanner has
    // to say what happened so the user isn't left wondering why a client just restarted.
    [Fact]
    public async Task HandleDiscordJoinAsync_NoIdleAccounts_TakesOverTheRunningOneAndSaysSo()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var row = Assert.Single(vm.Accounts);
        row.IsRunning = true; // the only account is already playing -- nothing idle to pick

        var ok = await vm.HandleDiscordJoinAsync(
            new LaunchTarget.GameJob(140403681187145, "job-a"), _ => true);

        Assert.True(ok);
        Assert.Single(launcher.Launches); // the join still happened
        Assert.Contains("takes over", vm.StatusBanner, StringComparison.OrdinalIgnoreCase);
    }
}
