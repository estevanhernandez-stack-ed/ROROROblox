using ROROROblox.App.Discord;
using ROROROblox.Core;

namespace ROROROblox.Tests.Discord;

public class JoinDispatchTests
{
    [Fact]
    public async Task HandleDiscordJoinAsync_DiscordOrigin_PrivateTarget_StillConfirms()
    {
        // Este's call: private servers are joinable, and the joiner is told they may bounce.
        // Roblox does the permission check server-side, so a mystery failure is the alternative.
        // Fix round 2: this is the regression guard for "private server always confirms,
        // regardless of origin" -- the in-client Join button (DiscordClient) is the trusted
        // origin, and it STILL has to confirm here because the destination, not the origin, is
        // what's risky about a private server.
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        string? shown = null;
        var target = new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode);

        await vm.HandleDiscordJoinAsync(target, JoinOrigin.DiscordClient, msg => { shown = msg; return true; });

        Assert.NotNull(shown);
        Assert.Contains("denied entry", shown, StringComparison.OrdinalIgnoreCase);
    }

    // Fix round 2: the regression guard named in the coordinator's message -- this is what stops
    // someone later "simplifying" the origin check into confirming everything and adding a click
    // to the normal, already-trusted in-client Join path.
    [Fact]
    public async Task HandleDiscordJoinAsync_DiscordOrigin_PublicTarget_DoesNotConfirm()
    {
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        var confirmed = false;

        await vm.HandleDiscordJoinAsync(
            new LaunchTarget.GameJob(140403681187145, "job-a"), JoinOrigin.DiscordClient, _ => { confirmed = true; return true; });

        Assert.False(confirmed);
    }

    // Fix round 2: the new case -- a roblox-rororo: URI can be triggered by any local process,
    // .url file, or browser navigation, so it confirms even for a public server. Origin, not
    // destination risk.
    [Fact]
    public async Task HandleDiscordJoinAsync_UriOrigin_PublicTarget_Confirms()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var row = Assert.Single(vm.Accounts);
        string? shown = null;

        var ok = await vm.HandleDiscordJoinAsync(
            new LaunchTarget.GameJob(140403681187145, "job-a"), JoinOrigin.UriHandler, msg => { shown = msg; return true; });

        Assert.True(ok);
        Assert.NotNull(shown);
        // Names the account (RenderName) and is plain about the request being unverifiable --
        // does NOT claim it might be "denied entry" (that's the private-server-specific copy,
        // and this target isn't a private server).
        Assert.Contains(row.RenderName, shown, StringComparison.Ordinal);
        Assert.Contains("outside RoRoRo", shown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("denied entry", shown, StringComparison.OrdinalIgnoreCase);
        Assert.Single(launcher.Launches);
    }

    [Fact]
    public async Task HandleDiscordJoinAsync_UriOrigin_DeclinedConfirm_LaunchesNothing()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();

        var ok = await vm.HandleDiscordJoinAsync(
            new LaunchTarget.GameJob(140403681187145, "job-a"), JoinOrigin.UriHandler, _ => false);

        Assert.False(ok);
        Assert.Empty(launcher.Launches);
    }

    // Both conditions at once (URI origin AND private server) must show exactly ONE prompt --
    // the private-server one, since it already carries the stronger denied-entry warning.
    [Fact]
    public async Task HandleDiscordJoinAsync_UriOrigin_PrivateTarget_ShowsOnlyThePrivateServerPrompt()
    {
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        var confirmCalls = 0;
        string? shown = null;
        var target = new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode);

        await vm.HandleDiscordJoinAsync(target, JoinOrigin.UriHandler, msg =>
        {
            confirmCalls++;
            shown = msg;
            return true;
        });

        Assert.Equal(1, confirmCalls);
        Assert.Contains("denied entry", shown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleDiscordJoinAsync_UserDeclinesTheWarning_LaunchesNothing()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var target = new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode);

        var ok = await vm.HandleDiscordJoinAsync(target, JoinOrigin.DiscordClient, _ => false);

        Assert.False(ok);
        Assert.Empty(launcher.Launches);
    }

    // FIX 8 (final whole-branch review, 2026-08-03): the row is picked BEFORE the confirm
    // decision, so row.IsRunning is already known when the prompt is built. When true, the
    // prompt itself says a running session is about to be taken over -- not only the
    // StatusBanner, which appears AFTER the user has already agreed to something else.
    [Fact]
    public async Task HandleDiscordJoinAsync_ConfirmMentionsTakeover_WhenTheChosenRowIsAlreadyRunning()
    {
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        var row = Assert.Single(vm.Accounts);
        row.IsRunning = true; // the only account is already running
        string? shown = null;
        var target = new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode);

        await vm.HandleDiscordJoinAsync(target, JoinOrigin.DiscordClient, msg => { shown = msg; return false; });

        Assert.NotNull(shown);
        Assert.Contains("takes over", shown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(row.RenderName, shown, StringComparison.Ordinal);
        // The private-server warning must still be there too -- this is additive, not a swap.
        Assert.Contains("denied entry", shown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleDiscordJoinAsync_NoAccountsConfigured_IsAnEmptyStateNotAnError()
    {
        var (vm, _) = DiscordTestHarness.VmWithNoAccounts();

        var ok = await vm.HandleDiscordJoinAsync(new LaunchTarget.GameJob(1, "j"), JoinOrigin.DiscordClient, _ => true);

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
            new LaunchTarget.GameJob(140403681187145, "job-a"), JoinOrigin.DiscordClient, _ => true);

        Assert.True(ok);
        Assert.Single(launcher.Launches); // the join still happened
        Assert.Contains("takes over", vm.StatusBanner, StringComparison.OrdinalIgnoreCase);
    }
}
