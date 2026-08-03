using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.App.Discord.Internal;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class RosterSnapshotProjectionTests
{
    /// <summary>
    /// Local copy of the fake used in <c>DiscordPresenceServiceTests</c> — not shared, per this
    /// suite's convention of not reaching into other test classes' private nested fixtures.
    /// </summary>
    private sealed class FakeRpcClient : IDiscordRpcClient
    {
        public List<DiscordPresencePayload> Presences { get; } = [];
        public int ClearCount { get; private set; }
        public bool IsInitialized { get; private set; }
        public void Initialize() => IsInitialized = true;
        public void Deinitialize() => IsInitialized = false;
        public void SetPresence(DiscordPresencePayload p) => Presences.Add(p);
        public void ClearPresence() => ClearCount++;
        public void Dispose() { }
        public event EventHandler<string>? JoinRequested;
        public event EventHandler? ConnectionFailed;
        public event EventHandler? Ready;
        public event EventHandler<string>? Errored;
    }

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

        Assert.Equal("job-a", account.Server!.Server.JobId);
    }

    // ---------- FIX 1 (final whole-branch review, 2026-08-03): private-server Join carrier ----------

    [Fact]
    public async Task PrivateServerRoster_EncodesAPSecretThatRoundTripsToTheSameCodeAndKind()
    {
        // Before the fix, PresencePayloadBuilder.JoinableServer carried only the (place, job)
        // pair -- never the private-server credential the session was actually launched with --
        // so DiscordPresenceService.Refresh() always encoded a public g| secret, even for a
        // private-server roster. A friend clicking Join then received a target Roblox bounced
        // server-side, with no warning ever shown. RosterServer.TryFrom (fed by
        // AccountSummary.LastLaunchTarget via BuildRosterSnapshot) is what lets a p| secret
        // reach the wire at all.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.CurrentServer = new ServerInstance(8737899170, "job-private");
        row.LastLaunchTarget = new LaunchTarget.PrivateServer(8737899170, "SECRETCODE", PrivateServerCodeKind.AccessCode);

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
        Assert.Equal(PrivateServerCodeKind.AccessCode, decoded.Kind);
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
        // denied-entry warning never showed.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        row.CurrentServer = new ServerInstance(8737899170, "job-private");
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
        // With only one account and it dropping out of game, the roster is now empty, so the
        // service's Refresh() should CLEAR the presence rather than leave the stale "in game" push
        // in place. Deleting the DiscordPresence?.Refresh() line from ApplySessionLimited was
        // verified BY EXPERIMENT to make this assertion fail (see task-6-report.md, fix round 2) —
        // ClearCount stays 0 and rpc.Presences keeps only the original in-game push.
        var (vm, row) = DiscordTestHarness.VmWithOneInGameAccount(realName: "a", maskedName: "a");
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, vm.BuildRosterSnapshot, NullLogger.Instance);
        vm.DiscordPresence = svc;
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        Assert.Single(rpc.Presences); // sanity: the in-game account already pushed once
        Assert.Equal(0, rpc.ClearCount);

        vm.ApplySessionLimited(row.Id);

        Assert.Equal(1, rpc.ClearCount);
    }
}
