using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.App.Discord.Internal;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class RosterSnapshotProjectionTests
{
    // FakeRpcClient lives in its own file — see FakeRpcClient.cs. It used to be copied per-suite;
    // three copies of one seam double is three chances for them to disagree about what the seam does.

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
    public void BuildRosterSnapshot_CarriesStreamerModeState_FromTheSameProviderRenderNameUses()
    {
        // Same source of truth as RenderName -- not a second flag invented for presence.
        // DiscordTestHarness's fake starts active (matching its RenderName-masking default), so
        // this also proves the flag is actually read, not just always false/true by construction.
        var (vm, _) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");

        Assert.True(vm.BuildRosterSnapshot().IsStreamerModeActive);

        vm.StreamerModeOn = false;

        Assert.False(vm.BuildRosterSnapshot().IsStreamerModeActive);
    }

    [Fact]
    public void BuildRosterSnapshot_CarriesTheServerFromPresence()
    {
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.CurrentServer = new ServerInstance(140403681187145, "job-a");

        var account = Assert.Single(vm.BuildRosterSnapshot().Accounts);

        Assert.Equal("job-a", account.Server!.Server.JobId);
    }

    // ---------- FIX 1 (final whole-branch review, 2026-08-03): private-server Join carrier ----------

    [Theory]
    [InlineData(PrivateServerCodeKind.AccessCode)]
    [InlineData(PrivateServerCodeKind.LinkCode)]
    public async Task PrivateServerInAUniverseThatTeleports_StillPublishesThePrivateSecret(PrivateServerCodeKind kind)
    {
        // THE test for the re-review's blocking finding. Pet Simulator 99 teleports players
        // between places INSIDE one universe -- entry place 8737899170, presence reports the
        // account in 140403681187145 (measured 2026-08-02, see ServerInstance's doc comment and
        // ServerInstanceTargetingTests' "THE test"). A private-server launch records the ENTRY
        // place in LastLaunchTarget; presence then reports the TELEPORTED place. The pre-fix
        // RosterServer.TryFrom compared ps.PlaceId == server.PlaceId, which is false for every
        // Pet Sim private-server session -- the exact audience this feature exists for -- so the
        // private code was silently dropped and Refresh() published a public g| secret instead.
        // The old version of this test hid the bug entirely by setting CurrentServer to the SAME
        // (entry) place as LastLaunchTarget, which never happens live.
        //
        // Both PrivateServerCodeKind values are exercised (Theory) because AccessCode and
        // LinkCode are not interchangeable on the wire -- Roblox returns permission-denied when
        // one is sent in the other's slot, so a kind lost in the carrier is a silent failure of
        // its own, distinct from the code being lost entirely.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.CurrentServer = new ServerInstance(140403681187145, "job-teleported"); // presence: teleported place
        row.LastLaunchTarget = new LaunchTarget.PrivateServer(8737899170, "SECRETCODE", kind); // entry place

        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, vm.BuildRosterSnapshot, NullLogger.Instance);
        vm.DiscordPresence = svc;
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });

        var party = Assert.Single(rpc.Presences).Party;
        Assert.NotNull(party);
        Assert.StartsWith("p|", party!.JoinSecret, StringComparison.Ordinal);

        Assert.True(JoinSecretCodec.TryDecode(party.JoinSecret, out var target));
        var decoded = Assert.IsType<LaunchTarget.PrivateServer>(target);
        Assert.Equal("SECRETCODE", decoded.Code);
        Assert.Equal(kind, decoded.Kind);
        // The secret must carry the PRIVATE SERVER'S OWN place id (the entry place the code was
        // actually issued for), never presence's teleported place -- joining that access code
        // under the wrong place produces Roblox's "server became unavailable" error.
        Assert.Equal(8737899170, decoded.PlaceId);
        Assert.NotEqual(row.CurrentServer.PlaceId, decoded.PlaceId);
    }

    [Fact]
    public async Task StalePrivateLaunchTarget_AfterLeavingForAPublicServer_PublishesAPublicSecret()
    {
        // MINOR 1 (re-review): LastLaunchTarget is written once per launch and never cleared on
        // its own. Once the blocking fix stops requiring presence to agree on place, a private
        // code recorded by an earlier launch must not silently keep attaching itself to whatever
        // server the account is in NOW. The account fully leaving the game (presence: not in
        // game) before joining a different, public server -- even one at the SAME place -- is
        // the signal that invalidates the stale credential; ApplyPresence's not-in-game branch
        // clears LastLaunchTarget alongside CurrentServer for exactly this reason.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.CurrentServer = new ServerInstance(8737899170, "job-private");
        row.LastLaunchTarget = new LaunchTarget.PrivateServer(8737899170, "STALECODE", PrivateServerCodeKind.AccessCode);

        vm.ApplyPresence(new AccountPresenceEventArgs(
            row.Id, UserPresenceType.Offline, null, null, DateTimeOffset.UtcNow));
        vm.ApplyPresence(new AccountPresenceEventArgs(
            row.Id, UserPresenceType.InGame, placeId: 8737899170, gameName: "Pet Simulator 99!",
            occurredAtUtc: DateTimeOffset.UtcNow, server: new ServerInstance(8737899170, "job-public")));

        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, vm.BuildRosterSnapshot, NullLogger.Instance);
        vm.DiscordPresence = svc;
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });

        var party = Assert.Single(rpc.Presences).Party;
        Assert.NotNull(party);
        Assert.StartsWith("g|", party!.JoinSecret, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicRoster_StillEncodesAGSecret()
    {
        // The other half of the same fix: a session that was never launched into a private
        // server (no matching LastLaunchTarget) must keep publishing the plain g| secret --
        // RosterServer.TryFrom must not invent a private code where none exists.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.CurrentServer = new ServerInstance(140403681187145, "job-public");

        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, vm.BuildRosterSnapshot, NullLogger.Instance);
        vm.DiscordPresence = svc;
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });

        var party = Assert.Single(rpc.Presences).Party;
        Assert.NotNull(party);
        Assert.StartsWith("g|", party!.JoinSecret, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrivateServerPresence_JoinReachesTheDeniedEntryConfirm()
    {
        // End to end: decode the ACTUAL secret a private-server roster publishes, then feed it
        // through HandleDiscordJoinAsync exactly as the in-client Join button would. This is the
        // regression guard for the whole-branch finding -- before the fix this secret decoded to
        // a LaunchTarget.GameJob, HandleDiscordJoinAsync saw no PrivateServer, and the
        // denied-entry warning never showed. Uses the real measured teleporting pair (entry vs.
        // presence place) rather than a matching one -- see
        // PrivateServerInAUniverseThatTeleports_StillPublishesThePrivateSecret's remarks.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.CurrentServer = new ServerInstance(140403681187145, "job-teleported");
        row.LastLaunchTarget = new LaunchTarget.PrivateServer(8737899170, "CODE", PrivateServerCodeKind.LinkCode);

        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, vm.BuildRosterSnapshot, NullLogger.Instance);
        vm.DiscordPresence = svc;
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });

        var secret = Assert.Single(rpc.Presences).Party!.JoinSecret;
        Assert.True(JoinSecretCodec.TryDecode(secret, out var target));

        string? shown = null;
        // Decline the launch (return false) -- this test only needs to prove the confirm was
        // reached with the right copy, not exercise a real launch through the fake launcher.
        await vm.HandleDiscordJoinAsync(target, JoinOrigin.DiscordClient, msg => { shown = msg; return false; });

        Assert.NotNull(shown);
        Assert.Contains("denied entry", shown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRosterSnapshot_OutOfGameAccounts_AreMarkedNotInGame()
    {
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.PresenceState = UserPresenceType.Offline;

        Assert.False(Assert.Single(vm.BuildRosterSnapshot().Accounts).InGame);
    }

    [Fact]
    public async Task SessionLimited_PushesAClearedPresence_ProvingTheMissingRefreshRegressionIsCovered()
    {
        // Fix round 2, carried finding: fix round 1's session-limited test only proved
        // BuildRosterSnapshot's InGame projection (never broken) — it never invoked the handler
        // that was actually missing the fix, DiscordPresence?.Refresh(), so it would have passed
        // unchanged against the pre-fix commit. This test drives the real path end to end: a real
        // DiscordPresenceService over a fake Discord RPC client, wired to vm.DiscordPresence, with
        // one account already in game (one push already observed). Then it calls
        // MainViewModel.ApplySessionLimited — the internal body OnAccountSessionLimited marshals to
        // via Application.Current?.Dispatcher.Invoke, same seam shape as ApplyPresence /
        // ApplySessionExpired. Calling the raw OnAccountSessionLimited event handler directly is
        // impractical in a headless test: Application.Current is null off a real WPF host, so
        // `Application.Current?.Dispatcher.Invoke(...)` silently no-ops and nothing under test would
        // ever run — this repo's established fix for that exact problem is the internal body method,
        // which this test calls instead.
        //
        // With only one account and it dropping out of game, the roster is now empty, so Refresh()
        // must push AGAIN rather than leave the stale "in game" card standing. Deleting the
        // DiscordPresence?.Refresh() line from ApplySessionLimited was verified BY EXPERIMENT to
        // make this assertion fail (see task-6-report.md, fix round 2).
        //
        // What that second push CONTAINS changed after the idle-presence decision (2026-08-03): the
        // service used to call ClearPresence when the roster emptied, and now publishes a deliberate
        // idle payload instead, because the RPC connection stays open either way and a cleared entry
        // renders as a blank card. The regression this test exists to catch is unchanged — a missing
        // Refresh means no second push at all — so the assertion moved from "cleared once" to "pushed
        // again, and the new push is the idle one." Asserting only the count would let a stale
        // in-game payload pass, so the content is checked too.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, vm.BuildRosterSnapshot, NullLogger.Instance);
        vm.DiscordPresence = svc;
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        Assert.Single(rpc.Presences); // sanity: the in-game account already pushed once
        Assert.Equal(0, rpc.ClearCount);

        vm.ApplySessionLimited(row.Id);

        Assert.Equal(2, rpc.Presences.Count);
        var afterDropout = rpc.Presences[^1];
        Assert.Equal("idle_large", afterDropout.LargeImageKey);
        Assert.Null(afterDropout.Party);          // an idle entry is never joinable
        Assert.Null(afterDropout.StartedAtUtc);   // and has no elapsed run
    }

    [Fact]
    public async Task StreamerModeToggle_RefreshesDiscordPresence_ProvingTheWiringIsCovered()
    {
        // Flipping streamer mode changes what BuildRosterSnapshot hands PresencePayloadBuilder --
        // masked-vs-real name, and now the anonymized-vs-honest roster count and party. A push
        // already sitting in Discord goes stale the instant the mode flips unless something calls
        // DiscordPresence.Refresh() from the same place that reacts to the flip
        // (MainViewModel.OnStreamerIdentityChanged). Same proof shape as
        // SessionLimited_PushesAClearedPresence_ProvingTheMissingRefreshRegressionIsCovered: a real
        // DiscordPresenceService over a fake Discord RPC client, wired to vm.DiscordPresence, so
        // this exercises the actual production handler rather than a hand-rolled stand-in for it.
        //
        // Verified by experiment (2026-08-03): commenting out the `DiscordPresence?.Refresh();`
        // line in OnStreamerIdentityChanged made this exact test fail with
        // "Assert.Equal() Failure: Values differ / Expected: 2 / Actual: 1" -- only the pre-toggle,
        // streamer-masked push was ever observed. Restored immediately after; see commit history.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "este_real", maskedName: "CaptainNoodle");
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, vm.BuildRosterSnapshot, NullLogger.Instance);
        vm.DiscordPresence = svc;
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });

        var beforeToggle = Assert.Single(rpc.Presences);
        // Streamer mode is active by default in this harness (mirrors RenderName's masked-by-
        // default fixture) -- the state line must not leak the roster's real headcount.
        Assert.DoesNotContain(beforeToggle.State!, char.IsDigit);

        vm.StreamerModeOn = false;

        Assert.Equal(2, rpc.Presences.Count);
        var afterToggle = rpc.Presences[^1];
        // "1 account" -- the real count is now visible, proving the second push actually reflects
        // the new state rather than being a duplicate of the first.
        Assert.Contains(afterToggle.State!, char.IsDigit);
    }
}
