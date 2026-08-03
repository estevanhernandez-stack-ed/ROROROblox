using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class RosterSnapshotProjectionTests
{
    [Fact]
    public void BuildRosterSnapshot_UsesRenderName_SoStreamerModeIsHonoredOutbound()
    {
        // THE test for this task. Streamer mode hides names INSIDE RoRoRo; if presence read
        // DisplayName instead of RenderName, a streamer would flip it on, feel covered, and
        // broadcast their real alt names to everyone watching their Discord. Same promise,
        // honored on the way out the door.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");

        var snapshot = vm.BuildRosterSnapshot();

        var account = Assert.Single(snapshot.Accounts);
        Assert.Equal("CaptainNoodle", account.DisplayName);
        Assert.DoesNotContain("este_real", account.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRosterSnapshot_CarriesTheServerFromPresence()
    {
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.CurrentServer = new ServerInstance(140403681187145, "job-a");

        var account = Assert.Single(vm.BuildRosterSnapshot().Accounts);

        Assert.Equal("job-a", account.Server!.JobId);
    }

    [Fact]
    public void BuildRosterSnapshot_OutOfGameAccounts_AreMarkedNotInGame()
    {
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.PresenceState = UserPresenceType.Offline;

        Assert.False(Assert.Single(vm.BuildRosterSnapshot().Accounts).InGame);
    }

    [Fact]
    public void BuildRosterSnapshot_SessionLimitedAccount_NoLongerReportsInGame()
    {
        // Fix round 1, finding 3: OnAccountSessionLimited (MainViewModel.cs) is a private handler
        // wired to IPresenceService.AccountSessionLimited — the harness's FakePresenceService
        // exposes that event as a no-op add/remove sink (matching MainViewModelTests' fixture), so
        // there is no way to raise it from a test and land inside the real handler. Per the fix
        // instructions, this reproduces the handler's own field mutations directly instead:
        // SessionLimited = true, PresenceState = Offline, CurrentGameName = null,
        // InGameSinceUtc = null — verbatim from OnAccountSessionLimited's body — then asserts the
        // projection reflects it. Without MainViewModel calling DiscordPresence?.Refresh() from
        // that handler, a friend watching Discord would keep seeing a rate-limited account as
        // in-game until some unrelated roster event happened to fire next.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");

        row.SessionLimited = true;
        row.PresenceState = UserPresenceType.Offline;
        row.CurrentGameName = null;
        row.InGameSinceUtc = null;

        Assert.False(Assert.Single(vm.BuildRosterSnapshot().Accounts).InGame);
    }
}
