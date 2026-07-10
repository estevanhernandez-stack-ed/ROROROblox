using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class StartupGateEvaluateTests
{
    private sealed class FakeProbe : IRobloxRunningProbe
    {
        public System.Collections.Generic.IReadOnlyList<RobloxProcessInfo> Players { get; set; }
            = System.Array.Empty<RobloxProcessInfo>();
        public System.Exception? Throw { get; set; }
        public System.Collections.Generic.IReadOnlyList<RobloxProcessInfo> GetRunningPlayers()
            => Throw is null ? Players : throw Throw;
        public System.Collections.Generic.IReadOnlyList<int> GetRunningPlayerPids()
            => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(GetRunningPlayers(), p => p.Pid));
    }

    [Fact]
    public void Evaluate_MutexNotAcquired_ReturnsBlocked()
    {
        var gate = new StartupGate(new FakeProbe());
        Assert.IsType<StartupGateResult.Blocked>(gate.Evaluate(mutexAcquired: false));
    }

    [Fact]
    public void Evaluate_AcquiredNoProcesses_ReturnsClean()
    {
        var gate = new StartupGate(new FakeProbe());
        Assert.IsType<StartupGateResult.Clean>(gate.Evaluate(mutexAcquired: true));
    }

    [Fact]
    public void Evaluate_AcquiredWithProcesses_ReturnsLeftoverWithSplit()
    {
        var probe = new FakeProbe
        {
            Players = new[]
            {
                new RobloxProcessInfo(1, HasWindow: false),
                new RobloxProcessInfo(2, HasWindow: false),
                new RobloxProcessInfo(3, HasWindow: true),
            },
        };
        var result = gate_Evaluate(probe, true);
        var leftover = Assert.IsType<StartupGateResult.Leftover>(result);
        Assert.Equal(2, leftover.Windowless);
        Assert.Equal(1, leftover.Windowed);
    }

    [Fact]
    public void Evaluate_AcquiredButProbeThrows_FailsOpenToClean()
    {
        var probe = new FakeProbe { Throw = new System.InvalidOperationException("scan mid-enum") };
        // acquired => we hold the lock => proceeding is safe even if we can't count leftovers
        Assert.IsType<StartupGateResult.Clean>(gate_Evaluate(probe, true));
    }

    // =====================================================================
    // The two ways of losing the singleton name mean opposite things. Roblox
    // holding it (as an Event) genuinely disables multi-instance; a compatible
    // tool holding it (as a Mutex) means Roblox already lost its singleton, so
    // everything works and blocking the user is wrong.
    // =====================================================================

    [Fact]
    public void Evaluate_HeldByRoblox_ReturnsBlocked()
    {
        var gate = new StartupGate(new FakeProbe());
        Assert.IsType<StartupGateResult.Blocked>(gate.Evaluate(MutexAcquireOutcome.HeldByRoblox));
    }

    [Fact]
    public void Evaluate_HeldByCompatibleTool_ReturnsSharedLock_NotBlocked()
    {
        var gate = new StartupGate(new FakeProbe());
        var result = gate.Evaluate(MutexAcquireOutcome.HeldByCompatibleTool);

        Assert.IsType<StartupGateResult.SharedLock>(result);
        Assert.IsNotType<StartupGateResult.Blocked>(result); // must not throw a modal at the user
    }

    [Fact]
    public void Evaluate_UnrecognizedFailure_BlocksConservatively()
    {
        var gate = new StartupGate(new FakeProbe());
        Assert.IsType<StartupGateResult.Blocked>(gate.Evaluate(MutexAcquireOutcome.Failed));
    }

    [Fact]
    public void Evaluate_Acquired_StillWalksTheLeftoverScan()
    {
        var probe = new FakeProbe { Players = new[] { new RobloxProcessInfo(1, HasWindow: true) } };
        var leftover = Assert.IsType<StartupGateResult.Leftover>(
            new StartupGate(probe).Evaluate(MutexAcquireOutcome.Acquired));
        Assert.Equal(1, leftover.Windowed);
    }

    private static StartupGateResult gate_Evaluate(IRobloxRunningProbe probe, bool acquired)
        => new StartupGate(probe).Evaluate(acquired);
}
