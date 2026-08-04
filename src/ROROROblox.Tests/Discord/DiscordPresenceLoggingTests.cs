using ROROROblox.App.Discord;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// The diagnostic gap this suite closes: before it, the Discord path logged only failures. A
/// working presence and a dead pipe produced byte-identical logs (nothing at all), which cost
/// three separate live-debugging detours on 2026-08-03 — every one of them starting from "the log
/// can't tell us whether it ever connected."
/// <para>
/// The second thing asserted here is the constraint that makes the first one safe: presence
/// carries a Join secret, and a private-server Join secret embeds a real private-server code. A
/// log file is the easiest place in the app to leak one, and log files get pasted into Discord by
/// users asking for help. The push line reports THAT a secret was attached, never its contents.
/// </para>
/// </summary>
public class DiscordPresenceLoggingTests
{
    private static readonly ServerInstance ServerA = new(140403681187145, "job-a");

    private static RosterSnapshot Roster(params RosterAccount[] accounts) => new(accounts);

    private static RosterAccount Live(string name, LaunchTarget? lastLaunchTarget = null) =>
        new(Guid.NewGuid(), name, InGame: true, "Pet Simulator 99!",
            RosterServer.TryFrom(ServerA, lastLaunchTarget), DateTimeOffset.UtcNow);

    private static bool HasInformation(CapturingLogger<DiscordPresenceService> log, string fragment) =>
        log.Snapshot().Any(l =>
            l.StartsWith("[Information]", StringComparison.Ordinal) &&
            l.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task PushingPresence_LogsWhatActuallyWentToDiscord()
    {
        var log = new CapturingLogger<DiscordPresenceService>();
        var svc = new DiscordPresenceService(new FakeRpcClient(), () => Roster(Live("A"), Live("B")), log);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });

        // The state string is the one field a human can compare against what Discord is showing.
        Assert.True(HasInformation(log, "2 accounts in one server"),
            $"No Information line named the pushed state. Lines: {string.Join(" | ", log.Snapshot())}");
    }

    [Fact]
    public async Task PushingAPrivateServerJoin_ReportsThatASecretWentOut_ButNeverTheCode()
    {
        const string code = "b7f2c1a4-DO-NOT-LOG";
        var log = new CapturingLogger<DiscordPresenceService>();
        var privateTarget = new LaunchTarget.PrivateServer(8737899170, code, PrivateServerCodeKind.LinkCode);
        var svc = new DiscordPresenceService(new FakeRpcClient(), () => Roster(Live("A", privateTarget)), log);

        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true, JoinEnabled = true });

        Assert.All(log.Snapshot(), line =>
            Assert.DoesNotContain(code, line, StringComparison.OrdinalIgnoreCase));
        Assert.True(HasInformation(log, "join secret"),
            $"The push line must say a Join secret was attached. Lines: {string.Join(" | ", log.Snapshot())}");
    }

    [Fact]
    public async Task ConnectingAndDropping_AreBothLogged()
    {
        var log = new CapturingLogger<DiscordPresenceService>();
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), log);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        rpc.RaiseReady();
        rpc.RaiseConnectionFailed();

        Assert.True(HasInformation(log, "connected to discord"), "The connect was not logged.");
        Assert.True(HasInformation(log, "dropped"), "The drop was not logged.");
    }

    [Fact]
    public async Task Reconnecting_IsLoggedDistinctlyFromTheFirstConnect()
    {
        // The whole reason this suite exists: "did Discord's restart bring us back, and when?"
        // has to be answerable from the log alone, without a live repro session.
        var log = new CapturingLogger<DiscordPresenceService>();
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), log);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        rpc.RaiseReady();
        rpc.RaiseConnectionFailed();
        rpc.RaiseReady();

        Assert.True(HasInformation(log, "reconnected"),
            $"A second Ready must read as a reconnect. Lines: {string.Join(" | ", log.Snapshot())}");
    }

    [Fact]
    public async Task RepeatedIdenticalPushes_DoNotFloodTheLogAtInformation()
    {
        // Refresh() runs on every roster poll. If every push logged at Information the file would
        // be unreadable within a session, and an unreadable log is the gap this suite closes.
        var log = new CapturingLogger<DiscordPresenceService>();
        var svc = new DiscordPresenceService(new FakeRpcClient(), () => Roster(Live("A")), log);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        svc.Refresh();
        svc.Refresh();
        svc.Refresh();

        var pushLines = log.Snapshot().Count(l =>
            l.StartsWith("[Information]", StringComparison.Ordinal) &&
            l.Contains("presence →", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, pushLines);
    }

    [Fact]
    public async Task ThePushAfterAReconnect_IsLoggedEvenWhenTheRosterNeverChanged()
    {
        // Companion to the flood guard, and the case that actually matters during a Discord
        // restart: the roster is identical across the drop, so change-detection alone would
        // silence the one push that proves recovery worked.
        var log = new CapturingLogger<DiscordPresenceService>();
        var rpc = new FakeRpcClient();
        var svc = new DiscordPresenceService(rpc, () => Roster(Live("A")), log);
        await svc.ApplyAsync(new DiscordConfig { PresenceEnabled = true });

        rpc.RaiseConnectionFailed();
        rpc.RaiseReady();

        var pushLines = log.Snapshot().Count(l =>
            l.StartsWith("[Information]", StringComparison.Ordinal) &&
            l.Contains("presence →", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, pushLines);
    }
}
