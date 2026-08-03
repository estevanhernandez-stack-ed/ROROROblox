using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.App.Discord.Internal;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class DiscordPresenceServiceTests
{
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
        public void RaiseJoin(string secret) => JoinRequested?.Invoke(this, secret);
        public void RaiseConnectionFailed() => ConnectionFailed?.Invoke(this, EventArgs.Empty);
        public void RaiseReady() => Ready?.Invoke(this, EventArgs.Empty);
    }

    private static readonly ServerInstance ServerA = new(140403681187145, "job-a");

    private static RosterSnapshot Roster(params RosterAccount[] accounts) => new(accounts);

    private static RosterAccount Live(string name) =>
        new(Guid.NewGuid(), name, InGame: true, "Pet Simulator 99!",
            RosterServer.TryFrom(ServerA, lastLaunchTarget: null), DateTimeOffset.UtcNow);

    [Fact]
    public async Task ApplyAsync_PresenceDisabled_NeverInitializesTheClient()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("CaptainNoodle")), NullLogger.Instance);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = false });

        Assert.False(rpc.IsInitialized);
        Assert.Empty(rpc.Presences);
    }

    [Fact]
    public async Task ApplyAsync_PresenceEnabled_PushesTheRosterState()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A"), Live("B")), NullLogger.Instance);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });

        var pushed = Assert.Single(rpc.Presences);
        Assert.Equal("Pet Simulator 99!", pushed.Details);
        Assert.Equal("2 accounts in one server", pushed.State);
        Assert.NotNull(pushed.Party);
    }

    [Fact]
    public async Task ApplyAsync_JoinDisabled_PublishesPresenceWithoutAJoinSecret()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = false });

        Assert.Null(Assert.Single(rpc.Presences).Party);
    }

    [Fact]
    public async Task Refresh_NothingRunning_ClearsPresenceRatherThanShowingStaleState()
    {
        var rpc = new FakeRpcClient();
        var roster = Roster(Live("A"));
        var svc = new DiscordPresenceService(rpc, () => roster, NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        roster = Roster();          // everything closed
        svc.Refresh();

        Assert.Equal(1, rpc.ClearCount);
    }

    [Fact]
    public async Task JoinRequested_DecodesTheSecretIntoALaunchTarget()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });
        LaunchTarget? received = null;
        svc.JoinRequested += (_, t) => received = t;

        rpc.RaiseJoin("g|140403681187145|job-a");

        var job = Assert.IsType<LaunchTarget.GameJob>(received);
        Assert.Equal("job-a", job.JobId);
    }

    [Fact]
    public async Task JoinRequested_AfterTheUserDisablesJoin_IsIgnored()
    {
        // A friend's stale cached Join button or an in-flight click can still arrive on the seam
        // after Join is turned off. The same "offer a Join the user did not enable" failure that
        // Refresh() guards against outbound must also be guarded against inbound.
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = false });
        var fired = false;
        svc.JoinRequested += (_, _) => fired = true;

        rpc.RaiseJoin("g|140403681187145|job-a");

        Assert.False(fired);
    }

    [Fact]
    public async Task JoinRequested_UndecodableSecret_IsIgnoredNotThrown()
    {
        // A malformed secret from anywhere must not take the app down.
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });
        var fired = false;
        svc.JoinRequested += (_, _) => fired = true;

        rpc.RaiseJoin("not-a-secret");

        Assert.False(fired);
    }

    [Fact]
    public async Task ConnectionFailed_ReportsItInTheStatusLineWithoutThrowing()
    {
        // Discord not running is the common case, not an error state.
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        rpc.RaiseConnectionFailed();

        Assert.Contains("isn't running", svc.StatusLine, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- FIX 5 (final whole-branch review, 2026-08-03): status line honors "off" ----------

    [Fact]
    public async Task ConnectionFailed_AfterPresenceTurnedOff_DoesNotOverwriteTheOffStatus()
    {
        // Deinitialize() (called by ApplyAsync's presence-off branch) can trigger Lachee's pipe to
        // close, which can raise ConnectionFailed asynchronously afterward. That stray callback
        // must not contradict the "Presence is off." the user just asked for.
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = false });
        Assert.Equal("Presence is off.", svc.StatusLine);

        rpc.RaiseConnectionFailed();

        Assert.Equal("Presence is off.", svc.StatusLine);
    }

    [Fact]
    public async Task Ready_AfterPresenceTurnedOff_DoesNotOverwriteTheOffStatusOrPushAStrayRefresh()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = false });
        var pushedBeforeStrayReady = rpc.Presences.Count;

        rpc.RaiseReady();

        Assert.Equal("Presence is off.", svc.StatusLine);
        Assert.Equal(pushedBeforeStrayReady, rpc.Presences.Count);
    }

    // ---------- Fix round 1, Finding 2: StatusChanged / the honest transient ----------

    [Fact]
    public async Task ApplyAsync_PresenceEnabled_SetsAnHonestTransientStatusImmediately()
    {
        // FakeRpcClient.Initialize() doesn't raise Ready/ConnectionFailed on its own (tests call
        // RaiseReady/RaiseConnectionFailed explicitly) -- so at this point, in production, the real
        // outcome hasn't arrived yet either. StatusLine must not still read the PREVIOUS value
        // ("Presence is off.") while presence is actually turning on.
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        Assert.Equal("Connecting to Discord…", svc.StatusLine);
    }

    [Fact]
    public async Task StatusChanged_FiresOnEveryRealTransition()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        var seen = new List<string>();
        svc.StatusChanged += (_, _) => seen.Add(svc.StatusLine);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });
        rpc.RaiseReady();

        Assert.Equal(["Connecting to Discord…", "Connected to Discord."], seen);
    }

    [Fact]
    public async Task StatusChanged_DoesNotFireWhenTheValueIsUnchanged()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = false }); // already "Presence is off."
        var fired = false;
        svc.StatusChanged += (_, _) => fired = true;

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = false }); // same value again

        Assert.False(fired);
    }

    // ---------- FIX 3 (final whole-branch review, 2026-08-03): Ready refreshes the roster ----------

    [Fact]
    public async Task Ready_RefreshesFromTheCurrentRoster_NotWhateverWasCachedBeforeTheDrop()
    {
        // Spec §8: "Discord restarts mid-session -> Reconnect with backoff; presence restored
        // from current roster." Simulate a reconnect: the roster changes WHILE "disconnected"
        // (Discord dropped), then Ready fires again -- the push that follows must reflect the
        // roster AT Ready time, not whatever was pushed before the drop.
        var accounts = new List<RosterAccount> { Live("A") };
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster([.. accounts]), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        Assert.Single(rpc.Presences);
        Assert.Equal("1 account", rpc.Presences[0].State);

        accounts.Add(Live("B")); // roster changed before Discord came back

        rpc.RaiseReady();

        Assert.Equal(2, rpc.Presences.Count);
        Assert.Equal("2 accounts in one server", rpc.Presences[^1].State);
    }

    // ---------- Fix round 1, Finding 1: JoinEnabled mirror for InboundJoinDispatcher ----------

    [Fact]
    public async Task JoinEnabled_MirrorsTheLiveConfig()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);

        Assert.False(svc.JoinEnabled); // default DiscordConfig has JoinEnabled = false

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });
        Assert.True(svc.JoinEnabled);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = false });
        Assert.False(svc.JoinEnabled);
    }

    // FIX 7 (final whole-branch review, 2026-08-03): Presence and Join are independently
    // togglable in Preferences, so "Presence off, Join on" is a reachable combination. Before
    // this fix it published no Join button (Refresh already required PresenceEnabled) yet still
    // accepted inbound URI joins, because JoinEnabled mirrored the raw config flag alone.
    [Fact]
    public async Task JoinEnabled_IsFalseWhenPresenceIsOff_EvenIfJoinItselfIsOn()
    {
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = false, JoinEnabled = true });

        Assert.False(svc.JoinEnabled);
    }

    [Fact]
    public async Task JoinRequested_PresenceOffJoinOn_IsIgnored()
    {
        // The in-client Join button path must honor the same effective gate as JoinEnabled.
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), NullLogger.Instance);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = false, JoinEnabled = true });
        var fired = false;
        svc.JoinRequested += (_, _) => fired = true;

        rpc.RaiseJoin("g|140403681187145|job-a");

        Assert.False(fired);
    }
}
