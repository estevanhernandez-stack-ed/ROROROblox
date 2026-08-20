using ROROROblox.Tests.Rendering;
using Xunit;

namespace ROROROblox.Tests;

/// <summary>
/// The render guard had no tests of its own — it was a guard nobody guarded. These cover the parts
/// that can be exercised without spawning a real process: the pid wording, and the two messages'
/// obligation to name a cause and a fix.
/// <para>
/// The detection itself is not unit-testable without a process-lister seam, and adding one to a
/// dev-loop guard costs more than it returns. What IS testable is that a person who trips this
/// learns what tripped it and what to do — which is the entire reason the guard exists rather than
/// letting the tests fail on their own terms.
/// </para>
/// </summary>
public class RenderEnvironmentTests
{
    [Fact]
    public void OneOffenderReadsAsOnePid()
    {
        Assert.Equal("pid 4242", RenderEnvironment.Describe([4242]));
    }

    [Fact]
    public void SeveralOffendersAreCounted()
    {
        // Multi-instance is the normal state of this app's own product, so the plural has to read.
        Assert.Equal("3 instances, pids 1, 2, 3", RenderEnvironment.Describe([1, 2, 3]));
    }

    [Theory]
    [InlineData("app")]
    [InlineData("client")]
    public void EveryMessageNamesTheOffenderAndTheFix(string which)
    {
        var msg = which == "app"
            ? RenderEnvironment.AppMessage("pid 99")
            : RenderEnvironment.ClientMessage("pid 99");

        // The pid, so the reader can find it.
        Assert.Contains("pid 99", msg, StringComparison.Ordinal);
        // A close-this instruction, because a guard that only says "no" is a worse timeout.
        Assert.Contains("Close ", msg, StringComparison.Ordinal);
        // CI stated explicitly — otherwise the first person to see this assumes the build is broken.
        Assert.Contains("CI has no", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheClientMessageExplainsWhyAPassWouldNotHaveCountedEither()
    {
        // The one thing a reader will push back on: "it passed for me." The message has to answer
        // that up front or the guard reads as overreach and gets deleted.
        var msg = RenderEnvironment.ClientMessage("pid 99");

        Assert.Contains("PASS under starvation is not", msg, StringComparison.Ordinal);
        Assert.Contains("2026-08-20-render-test-flake-cause.md", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTwoCausesDoNotShareAnExplanation()
    {
        // They fail for genuinely different reasons — one is unreadable, one is starved. A shared
        // message would send whoever trips it looking in the wrong place.
        Assert.Contains("URI prefix", RenderEnvironment.AppMessage("pid 1"), StringComparison.Ordinal);
        Assert.DoesNotContain("URI prefix", RenderEnvironment.ClientMessage("pid 1"), StringComparison.Ordinal);
        Assert.Contains("saturates the GPU", RenderEnvironment.ClientMessage("pid 1"), StringComparison.Ordinal);
        Assert.DoesNotContain("saturates the GPU", RenderEnvironment.AppMessage("pid 1"), StringComparison.Ordinal);
    }

    [Fact]
    public void ACleanMachineRendersWithoutComplaint()
    {
        // Runs on CI and on any developer machine that closed both, which is the state the whole
        // suite assumes. If this one ever fails, the guard has become the problem.
        RenderEnvironment.RequireClean();
    }
}
