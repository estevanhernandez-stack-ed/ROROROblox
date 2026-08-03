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
        new(Guid.NewGuid(), name, InGame: true, "Pet Simulator 99!", ServerA, DateTimeOffset.UtcNow);

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
}
