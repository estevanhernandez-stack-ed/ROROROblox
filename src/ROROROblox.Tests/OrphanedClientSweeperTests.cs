using ROROROblox.App.Tray;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// F-103, the driver half. <c>RunningRobloxScanner.Scan</c> already finds untagged clients but runs
/// exactly once, at startup — so a client Roblox restarts for itself mid-session is never looked
/// for. These drive the sweep deterministically through its enumerate seam, because
/// <c>Process.GetProcessesByName</c> cannot be exercised in a unit test.
/// </summary>
public class OrphanedClientSweeperTests
{
    private static readonly Guid AccountA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);
    }

    private static OrphanedClientSweeper Build(
        FixedClock clock,
        IReadOnlyList<OrphanedClientSweeper.ClientSnapshot> clients,
        List<(Guid Account, int Pid)> adopted,
        Func<int, bool>? isTracked = null)
        => new(clock,
               isTracked ?? (_ => false),
               (a, p) => adopted.Add((a, p)),
               () => clients);

    [Fact]
    public void TheShippedDefect_ARestartedClientIsAdopted()
    {
        // Roblox updates: the tracked pid dies, a replacement appears with a bare title, and
        // nothing was looking for it. This is the whole of F-103 in one case.
        var clock = new FixedClock();
        var adopted = new List<(Guid, int)>();
        var sweeper = Build(clock, [new(200, clock.UtcNow.AddSeconds(6), "Roblox")], adopted);

        sweeper.OnClientExited(AccountA, 100);
        clock.UtcNow = clock.UtcNow.AddSeconds(8);
        sweeper.Sweep();

        Assert.Single(adopted);
        Assert.Equal((AccountA, 200), adopted[0]);
    }

    [Fact]
    public void ADecoratedClientIsNotAnOrphan()
    {
        // "Roblox - Este" is a window we already own. Treating it as an orphan would re-adopt a
        // client every sweep and fight the decorator for its own title.
        var clock = new FixedClock();
        var adopted = new List<(Guid, int)>();
        var sweeper = Build(clock, [new(200, clock.UtcNow.AddSeconds(6), "Roblox - Este")], adopted);

        sweeper.OnClientExited(AccountA, 100);
        clock.UtcNow = clock.UtcNow.AddSeconds(8);
        sweeper.Sweep();

        Assert.Empty(adopted);
    }

    [Fact]
    public void AClientTheTrackerAlreadyOwnsIsSkipped()
    {
        // Belt and braces with the title check: a tracked pid is ours whatever its title says.
        var clock = new FixedClock();
        var adopted = new List<(Guid, int)>();
        var sweeper = Build(clock, [new(200, clock.UtcNow.AddSeconds(6), "Roblox")], adopted, isTracked: pid => pid == 200);

        sweeper.OnClientExited(AccountA, 100);
        clock.UtcNow = clock.UtcNow.AddSeconds(8);
        sweeper.Sweep();

        Assert.Empty(adopted);
    }

    [Fact]
    public void AnOrphanWithNothingToSucceedIsLeftAlone()
    {
        // A client the user opened themselves, with nothing having exited. Adopting it would be
        // RoRoRo claiming a window it had nothing to do with.
        var clock = new FixedClock();
        var adopted = new List<(Guid, int)>();
        var sweeper = Build(clock, [new(200, clock.UtcNow, "Roblox")], adopted);

        clock.UtcNow = clock.UtcNow.AddSeconds(5);
        sweeper.Sweep();

        Assert.Empty(adopted);
    }

    [Fact]
    public void OneExitCannotBeClaimedTwice()
    {
        // After a successful adoption the predecessor is spent. Without that, a second orphan
        // appearing later would inherit the same account and two windows would claim one identity.
        var clock = new FixedClock();
        var adopted = new List<(Guid, int)>();
        var clients = new List<OrphanedClientSweeper.ClientSnapshot>
        {
            new(200, clock.UtcNow.AddSeconds(6), "Roblox"),
        };
        var sweeper = new OrphanedClientSweeper(clock, _ => false, (a, p) => adopted.Add((a, p)), () => clients);

        sweeper.OnClientExited(AccountA, 100);
        clock.UtcNow = clock.UtcNow.AddSeconds(8);
        sweeper.Sweep();
        Assert.Single(adopted);

        // A second bare client turns up later with no new exit behind it.
        clients.Clear();
        clients.Add(new(201, clock.UtcNow.AddSeconds(2), "Roblox"));
        clock.UtcNow = clock.UtcNow.AddSeconds(4);
        sweeper.Sweep();

        Assert.Single(adopted);
    }

    [Fact]
    public void TwoExitsAndTwoOrphans_NeitherIsGuessed()
    {
        // The rule that keeps a wrong name off a window. Declining here is correct, not a gap.
        var clock = new FixedClock();
        var adopted = new List<(Guid, int)>();
        var sweeper = Build(clock,
            [new(200, clock.UtcNow.AddSeconds(5), "Roblox"), new(201, clock.UtcNow.AddSeconds(6), "Roblox")],
            adopted);

        sweeper.OnClientExited(AccountA, 100);
        sweeper.OnClientExited(AccountB, 101);
        clock.UtcNow = clock.UtcNow.AddSeconds(8);
        sweeper.Sweep();

        Assert.Empty(adopted);
    }

    [Fact]
    public void AnExitOlderThanTheWindowNoLongerAdopts()
    {
        // Otherwise a client opened much later gets claimed by whoever last exited.
        var clock = new FixedClock();
        var adopted = new List<(Guid, int)>();
        var start = clock.UtcNow;
        var sweeper = Build(clock, [new(200, start + ClientSuccession.Window + TimeSpan.FromSeconds(30), "Roblox")], adopted);

        sweeper.OnClientExited(AccountA, 100);
        clock.UtcNow = start + ClientSuccession.Window + TimeSpan.FromSeconds(35);
        sweeper.Sweep();

        Assert.Empty(adopted);
    }

    [Fact]
    public void AnAdoptionThatThrowsDoesNotStopTheSweep()
    {
        // Attaching can fail for reasons outside our control (the pid died between enumerate and
        // adopt). A sweep that dies there stops finding every later orphan too.
        var clock = new FixedClock();
        var sweeper = new OrphanedClientSweeper(
            clock, _ => false, (_, _) => throw new InvalidOperationException("pid vanished"),
            () => [new(200, clock.UtcNow.AddSeconds(6), "Roblox")]);

        sweeper.OnClientExited(AccountA, 100);
        clock.UtcNow = clock.UtcNow.AddSeconds(8);

        sweeper.Sweep(); // must not throw
    }

    [Fact]
    public void NoClientsAtAllIsNotAnError()
    {
        var clock = new FixedClock();
        var adopted = new List<(Guid, int)>();
        var sweeper = Build(clock, [], adopted);

        sweeper.Sweep();

        Assert.Empty(adopted);
    }

    [Fact]
    public void APersistentOrphanIsReportedOnceNotEveryTick()
    {
        // FOUND IN LIVE VERIFICATION, not by the suite: the first wiring logged this warning every
        // 5 seconds forever. A client the user left open is permanent, so per-tick warnings bury
        // the log and train everyone to skip them — the same way a cap firing four times in eight
        // minutes gets muted. The single-Sweep tests above could not see it by construction.
        var clock = new FixedClock();
        var logged = new List<string>();
        var clients = new List<OrphanedClientSweeper.ClientSnapshot> { new(200, clock.UtcNow, "Roblox") };
        var sweeper = new OrphanedClientSweeper(
            clock, _ => false, (_, _) => { }, () => clients, new CountingLogger(logged));

        sweeper.Sweep();
        sweeper.Sweep();
        sweeper.Sweep();

        Assert.Single(logged);
    }

    [Fact]
    public void ANewOrphanIsStillNewsAfterOneWasAlreadyReported()
    {
        // Latching must not go silent about something it has never seen.
        var clock = new FixedClock();
        var logged = new List<string>();
        var clients = new List<OrphanedClientSweeper.ClientSnapshot> { new(200, clock.UtcNow, "Roblox") };
        var sweeper = new OrphanedClientSweeper(
            clock, _ => false, (_, _) => { }, () => clients, new CountingLogger(logged));

        sweeper.Sweep();
        clients.Add(new(201, clock.UtcNow, "Roblox"));
        sweeper.Sweep();

        Assert.Equal(2, logged.Count);
    }

    [Fact]
    public void TheLatchClearsWhenEverythingIsOwnedAgain()
    {
        // Otherwise the remembered set outlives the situation it described.
        var clock = new FixedClock();
        var logged = new List<string>();
        var clients = new List<OrphanedClientSweeper.ClientSnapshot> { new(200, clock.UtcNow, "Roblox") };
        var sweeper = new OrphanedClientSweeper(
            clock, _ => false, (_, _) => { }, () => clients, new CountingLogger(logged));

        sweeper.Sweep();
        clients.Clear();
        sweeper.Sweep();
        clients.Add(new(200, clock.UtcNow, "Roblox"));
        sweeper.Sweep();

        Assert.Equal(2, logged.Count);
    }

    /// <summary>Records only the warning text, which is what the latch governs.</summary>
    private sealed class CountingLogger(List<string> sink) : Microsoft.Extensions.Logging.ILogger<OrphanedClientSweeper>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning) sink.Add(formatter(state, exception));
        }
    }
}
