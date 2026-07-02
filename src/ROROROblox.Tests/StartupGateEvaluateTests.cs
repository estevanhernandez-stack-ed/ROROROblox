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

    private static StartupGateResult gate_Evaluate(IRobloxRunningProbe probe, bool acquired)
        => new StartupGate(probe).Evaluate(acquired);
}
