using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// F-111. The Stop button did nothing on the first click, measured against a real in-game client:
/// click one at 14:14:18, still running 30 seconds later; click two at 14:15:21, dead by 14:15:23.
/// <para>
/// These drive the sequence through an injected delay so no test sleeps, and through an injected
/// exit predicate so the "did it actually close" question — the one the old code never asked — is
/// the thing under test.
/// </para>
/// </summary>
public class ClientStopSequenceTests
{
    /// <summary>Returns immediately; the sequence's timing is not what these are about.</summary>
    private static Task NoDelay(TimeSpan _, CancellationToken __) => Task.CompletedTask;

    /// <summary>Exits after <paramref name="afterCalls"/> checks, so a test can say WHEN it closed.</summary>
    private static Func<bool> ExitsAfter(int afterCalls)
    {
        var n = 0;
        return () => ++n > afterCalls;
    }

    [Fact]
    public async Task AnAlreadyDeadClientIsNotAskedOrKilled()
    {
        var asked = false; var killed = false;
        var outcome = await ClientStopSequence.RunAsync(
            hasExited: () => true, askToClose: () => { asked = true; return true; },
            forceKill: () => killed = true, onSecondsRemaining: _ => { }, delay: NoDelay);

        Assert.Equal(ClientStopOutcome.AlreadyExited, outcome);
        Assert.False(asked);
        Assert.False(killed);
    }

    [Fact]
    public async Task WhenRobloxClosesItselfWeDoNotKillIt()
    {
        // The good path, and the whole point of F-109: a clean exit is the only exit that persists
        // the user's Roblox settings. Killing here would throw that away for no reason.
        var killed = false;
        var outcome = await ClientStopSequence.RunAsync(
            hasExited: ExitsAfter(3), askToClose: () => true,
            forceKill: () => killed = true, onSecondsRemaining: _ => { }, delay: NoDelay);

        Assert.Equal(ClientStopOutcome.ClosedItself, outcome);
        Assert.False(killed);
    }

    [Fact]
    public async Task TheShippedDefect_OneClickNowFinishesTheJob()
    {
        // Roblox ignores the close and the user never answers the in-game confirm. The old code
        // stopped here and left the client running; a single press must still end with it gone.
        var killed = false;
        var outcome = await ClientStopSequence.RunAsync(
            hasExited: () => false, askToClose: () => true,
            forceKill: () => killed = true, onSecondsRemaining: _ => { }, delay: NoDelay);

        Assert.Equal(ClientStopOutcome.Forced, outcome);
        Assert.True(killed);
    }

    [Fact]
    public async Task TheCountdownWalksFromTenToZeroWithoutSkipping()
    {
        var seen = new List<int>();
        await ClientStopSequence.RunAsync(
            hasExited: () => false, askToClose: () => true, forceKill: () => { },
            onSecondsRemaining: seen.Add, delay: NoDelay, grace: TimeSpan.FromSeconds(10));

        // 10 down to 0, each exactly once and in order — a countdown that jumps is worse than none.
        Assert.Equal(Enumerable.Range(0, 11).Reverse(), seen);
    }

    [Fact]
    public async Task TheCountdownEndsAtZeroEvenWhenRobloxClosesEarly()
    {
        // The row has to clear its waiting state on every path, or it keeps counting after the
        // client is gone.
        var seen = new List<int>();
        await ClientStopSequence.RunAsync(
            hasExited: ExitsAfter(2), askToClose: () => true, forceKill: () => { },
            onSecondsRemaining: seen.Add, delay: NoDelay, grace: TimeSpan.FromSeconds(10));

        Assert.Equal(0, seen[^1]);
    }

    [Fact]
    public async Task AutoStopStillAsksBeforeItForces()
    {
        // Zero grace is the user's "don't make me wait" setting. Asking first is free and, when
        // Roblox takes it (no session, or its confirm suppressed), the settings survive — so
        // skipping the ask would lose them for nothing.
        var asked = false;
        var outcome = await ClientStopSequence.RunAsync(
            hasExited: () => false, askToClose: () => { asked = true; return true; },
            forceKill: () => { }, onSecondsRemaining: _ => { }, delay: NoDelay,
            grace: TimeSpan.Zero);

        Assert.True(asked);
        Assert.Equal(ClientStopOutcome.Forced, outcome);
    }

    [Fact]
    public async Task AnExitLandingAfterTheLastTickIsNotAKill()
    {
        // The race the re-check exists for. Reporting Forced here would make the outcome a lie:
        // the client closed cleanly and its settings are on disk.
        var killed = false;
        var checks = 0;
        var outcome = await ClientStopSequence.RunAsync(
            hasExited: () => { checks++; return checks > 2; },   // alive for the loop, gone at the final re-check
            askToClose: () => true, forceKill: () => killed = true,
            onSecondsRemaining: _ => { }, delay: NoDelay, grace: TimeSpan.FromSeconds(1));

        Assert.Equal(ClientStopOutcome.ClosedItself, outcome);
        Assert.False(killed);
    }

    [Fact]
    public async Task CancellingStopsTheWaitAndStillForces()
    {
        // App shutdown cancels mid-countdown. Leaving the client alive there would strand it as an
        // orphan nothing owns — which is F-103's whole family of bugs.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var killed = false;

        var outcome = await ClientStopSequence.RunAsync(
            hasExited: () => false, askToClose: () => true, forceKill: () => killed = true,
            onSecondsRemaining: _ => { }, delay: NoDelay, ct: cts.Token);

        Assert.True(killed);
        Assert.Equal(ClientStopOutcome.Forced, outcome);
    }

    [Fact]
    public async Task ItAsksASecondTime_BecauseThatIsWhatActuallyClosesAnInGameClient()
    {
        // Corrected 2026-08-20 by the user: the first close raises Roblox's confirm, and a SECOND
        // close dismisses it into a clean exit — which is exactly what double-clicking the X does.
        // Measured directly: close at 15:34:24 left it alive, close at 15:34:28 exited it, and the
        // settings file came back holding the on-screen geometry, written by Roblox itself.
        // A stop that never asks twice waits the full grace and then kills for no reason.
        var asks = 0;
        await ClientStopSequence.RunAsync(
            hasExited: () => false, askToClose: () => { asks++; return true; },
            forceKill: () => { }, onSecondsRemaining: _ => { }, delay: NoDelay,
            grace: TimeSpan.FromSeconds(10));

        Assert.Equal(2, asks);
    }

    [Fact]
    public async Task ItNeverAsksAThirdTime()
    {
        // Twice is what a person does. Repeating on every tick would be hammering a window that
        // has already answered us, ten times in ten seconds.
        var asks = 0;
        await ClientStopSequence.RunAsync(
            hasExited: () => false, askToClose: () => { asks++; return true; },
            forceKill: () => { }, onSecondsRemaining: _ => { }, delay: NoDelay,
            grace: TimeSpan.FromSeconds(60));

        Assert.Equal(2, asks);
    }

    [Fact]
    public async Task AClientThatGoesBeforeTheSecondAskIsNotAskedAgain()
    {
        var asks = 0;
        var outcome = await ClientStopSequence.RunAsync(
            hasExited: ExitsAfter(1), askToClose: () => { asks++; return true; },
            forceKill: () => { }, onSecondsRemaining: _ => { }, delay: NoDelay);

        Assert.Equal(ClientStopOutcome.ClosedItself, outcome);
        Assert.Equal(1, asks);
    }
}
