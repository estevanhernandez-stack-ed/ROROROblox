using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Tests.Discord;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// F-106. Until 2026-08-21 the flows around this view model's remaining dialogs — the three
/// result-bearing pickers, the three interruptions, the rename prompt — could not execute in a
/// test at all: constructing the window was the untestable part, and the count of such methods
/// GREW by nine while the row that mentioned them was open. Each dialog is now a seam in the
/// <c>StopAllConfirm</c> pattern, and these are the first tests those flows have ever had. The
/// dialogs themselves stay covered by the render gates; what is asserted here is everything the
/// dialog's answer feeds.
/// </summary>
public class MainViewModelWindowSeamTests
{
    [Fact]
    public async Task SquadLaunch_PickerCancel_LaunchesNothing()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var asked = 0;
        vm.SquadLaunchPicker = (_, _, _) => { asked++; return null; };

        await vm.OpenSquadLaunchAsync();

        Assert.Equal(1, asked);
        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task SquadLaunch_PickedTarget_DispatchesTheEligibleAccount()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var target = new LaunchTarget.Place(8737899170);
        vm.SquadLaunchPicker = (_, _, _) => target;

        await vm.OpenSquadLaunchAsync();

        Assert.Equal([target], launcher.Launches);
    }

    [Fact]
    public async Task JoinByLink_PickerCancel_LaunchesNothing()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        vm.JoinByLinkPicker = _ => null;

        await vm.OpenJoinByLinkAsync(vm.Accounts.Single());

        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task JoinByLink_PickedTarget_LaunchesTheRowIntoIt()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var target = new LaunchTarget.Place(8737899170);
        vm.JoinByLinkPicker = renderName => (target, false);

        await vm.OpenJoinByLinkAsync(vm.Accounts.Single());

        Assert.Equal([target], launcher.Launches);
    }

    [Fact]
    public async Task FriendFollow_PickerCancel_LaunchesNothing()
    {
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var row = vm.Accounts.Single();
        row.RobloxUserId = 12345; // skip the profile resolve — the seam under test is the picker
        vm.FriendFollowPicker = (_, _, _) => null;

        await vm.OpenFriendFollowAsync(row);

        Assert.Empty(launcher.Launches);
    }

    [Fact]
    public async Task FriendFollow_HiddenPresence_IsBlockedByTheSharedGuard()
    {
        // The picker hands back a target whose presence the guard cannot verify (null snapshot —
        // privacy-hidden or stale). EvaluateFollow owns the launch decision, and this is the flow
        // that used to be unreachable in tests: the guard must block WITH a message, not bounce
        // the account to the Roblox home page.
        var (vm, launcher) = DiscordTestHarness.VmWithOneIdleAccount();
        var row = vm.Accounts.Single();
        row.RobloxUserId = 12345;
        vm.FriendFollowPicker = (_, _, _) =>
            new FriendFollowPick(new LaunchTarget.Place(999), Presence: null, FriendName: "Koii");

        await vm.OpenFriendFollowAsync(row);

        Assert.Empty(launcher.Launches);
        Assert.False(string.IsNullOrWhiteSpace(vm.StatusBanner));
        Assert.Contains("Koii", vm.StatusBanner, StringComparison.Ordinal);
    }

    [Fact]
    public void DpapiCorrupt_StartFresh_ClearsTheRosterAndSaysSo()
    {
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        Assert.Single(vm.Accounts);
        vm.DpapiCorruptPrompt = () => true;
        var quit = false;
        vm.QuitApplication = () => quit = true;

        vm.ShowDpapiCorruptModal();

        Assert.Empty(vm.Accounts);
        Assert.False(quit);
        Assert.Contains("Started fresh", vm.StatusBanner, StringComparison.Ordinal);
    }

    [Fact]
    public void DpapiCorrupt_Quit_LeavesTheRosterAndExits()
    {
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        vm.DpapiCorruptPrompt = () => false;
        var quit = false;
        vm.QuitApplication = () => quit = true;

        vm.ShowDpapiCorruptModal();

        Assert.True(quit);
        Assert.Single(vm.Accounts); // nothing cleared on the restore-a-backup path
    }

    [Fact]
    public async Task Rename_Save_ReachesTheStoreAndTheRow()
    {
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        var row = vm.Accounts.Single();
        vm.RenamePrompt = _ => Task.FromResult(new RenameResult(RenameResultKind.Save, "Mr. Solo Dolo"));

        await vm.RenameItemAsync(new RenameTarget(
            RenameTargetKind.Account, row.Id, row.RenderName, row.LocalName));

        Assert.Equal("Mr. Solo Dolo", row.LocalName);
    }

    [Fact]
    public async Task Rename_Cancel_ChangesNothing()
    {
        var (vm, _) = DiscordTestHarness.VmWithOneIdleAccount();
        var row = vm.Accounts.Single();
        vm.RenamePrompt = _ => Task.FromResult(new RenameResult(RenameResultKind.Cancel, null));

        await vm.RenameItemAsync(new RenameTarget(
            RenameTargetKind.Account, row.Id, row.RenderName, row.LocalName));

        Assert.Null(row.LocalName);
    }
}
