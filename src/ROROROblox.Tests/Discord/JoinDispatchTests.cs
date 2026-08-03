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
}
