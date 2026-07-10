using ROROROblox.App.AppLifecycle;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// Locks the load-bearing invariant of the BLOCKED-startup escape hatch: Start anyway proceeds
/// WITHOUT the mutex, Recovered proceeds holding it, Quit (and anything unrecognized) does not
/// proceed. See docs/superpowers/specs/2026-07-09-startup-start-anyway-design.md.
/// </summary>
public class BlockedStartupDecisionTests
{
    [Fact]
    public void Recovered_Proceeds_HoldingMutex()
    {
        var (proceed, holds) = BlockedStartupDecision.Resolve(BlockedModalOutcome.Recovered);
        Assert.True(proceed);
        Assert.True(holds);
    }

    [Fact]
    public void StartAnyway_Proceeds_WithoutMutex()
    {
        var (proceed, holds) = BlockedStartupDecision.Resolve(BlockedModalOutcome.StartAnyway);
        Assert.True(proceed);
        Assert.False(holds);
    }

    [Fact]
    public void Quit_DoesNotProceed()
    {
        var (proceed, holds) = BlockedStartupDecision.Resolve(BlockedModalOutcome.Quit);
        Assert.False(proceed);
        Assert.False(holds);
    }

    [Fact]
    public void UnrecognizedOutcome_FailsClosedToQuit()
    {
        var (proceed, holds) = BlockedStartupDecision.Resolve((BlockedModalOutcome)999);
        Assert.False(proceed);
        Assert.False(holds);
    }
}
