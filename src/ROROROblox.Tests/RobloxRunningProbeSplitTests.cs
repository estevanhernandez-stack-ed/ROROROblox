using System.Linq;
using ROROROblox.Core.Diagnostics;
using Xunit;

namespace ROROROblox.Tests;

public class RobloxRunningProbeSplitTests
{
    // A hand-rolled probe returning fixed data — proves GetRunningPlayerPids delegates to
    // GetRunningPlayers. The real RobloxRunningProbe's process enumeration is covered by
    // manual smoke (needs live processes), not this unit test.
    private sealed class FixedProbe : IRobloxRunningProbe
    {
        private readonly RobloxProcessInfo[] _players;
        public FixedProbe(params RobloxProcessInfo[] players) => _players = players;
        public System.Collections.Generic.IReadOnlyList<RobloxProcessInfo> GetRunningPlayers() => _players;
        public System.Collections.Generic.IReadOnlyList<int> GetRunningPlayerPids()
            => GetRunningPlayers().Select(p => p.Pid).ToArray();
    }

    [Fact]
    public void GetRunningPlayerPids_DelegatesToGetRunningPlayers()
    {
        var probe = new FixedProbe(
            new RobloxProcessInfo(101, HasWindow: true),
            new RobloxProcessInfo(202, HasWindow: false));

        Assert.Equal(new[] { 101, 202 }, probe.GetRunningPlayerPids());
    }

    [Fact]
    public void GetRunningPlayers_CarriesWindowedFlag()
    {
        var probe = new FixedProbe(
            new RobloxProcessInfo(101, HasWindow: true),
            new RobloxProcessInfo(202, HasWindow: false));

        var players = probe.GetRunningPlayers();
        Assert.Equal(1, players.Count(p => p.HasWindow));
        Assert.Equal(1, players.Count(p => !p.HasWindow));
    }
}
