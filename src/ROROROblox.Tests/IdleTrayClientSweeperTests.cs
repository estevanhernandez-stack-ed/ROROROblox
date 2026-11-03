using ROROROblox.App.Tray;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>#222. The sweeper drives the stopper with the 2-minute floor on its own timer.</summary>
public class IdleTrayClientSweeperTests
{
    private sealed class RecordingStopper : IRobloxInstanceStopper
    {
        public readonly List<(bool Tracked101, TimeSpan? MinAge)> Calls = [];
        public Exception? Throw;
        public int StopIdleTrayClients(Func<int, bool> isTracked, TimeSpan? minAge)
        {
            if (Throw is not null) throw Throw;
            lock (Calls) Calls.Add((isTracked(101), minAge));
            return 0;
        }
        public int StopAll() => 0;
        public int StopWindowless() => 0;
        public bool StopAccount(Guid accountId) => false;
    }

    [Fact]
    public void Constants_AreThirtySecondsAndTwoMinutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), IdleTrayClientSweeper.SweepInterval);
        Assert.Equal(TimeSpan.FromMinutes(2), IdleTrayClientSweeper.MinAge);
    }

    [Fact]
    public void SweepOnce_PassesTwoMinuteFloor_AndTrackedSnapshot()
    {
        var stopper = new RecordingStopper();
        using var sweeper = new IdleTrayClientSweeper(stopper, () => new HashSet<int> { 101 });

        sweeper.SweepOnce();

        var call = Assert.Single(stopper.Calls);
        Assert.True(call.Tracked101);
        Assert.Equal(TimeSpan.FromMinutes(2), call.MinAge);
    }

    [Fact]
    public void SweepOnce_SwallowsAStopperFailure()
    {
        var stopper = new RecordingStopper { Throw = new InvalidOperationException("boom") };
        using var sweeper = new IdleTrayClientSweeper(stopper, () => new HashSet<int>());
        Assert.Equal(0, sweeper.SweepOnce());
    }

    [Fact]
    public async Task Start_TicksOnItsOwnTimer_AndDisposeStopsIt()
    {
        var stopper = new RecordingStopper();
        var sweeper = new IdleTrayClientSweeper(stopper, () => new HashSet<int>(), TimeSpan.FromMilliseconds(20));
        sweeper.Start();
        for (var i = 0; i < 100; i++)
        {
            lock (stopper.Calls) { if (stopper.Calls.Count >= 2) break; }
            await Task.Delay(20);
        }
        sweeper.Dispose();
        int after;
        lock (stopper.Calls) after = stopper.Calls.Count;
        Assert.True(after >= 2);

        await Task.Delay(150);
        lock (stopper.Calls) Assert.True(stopper.Calls.Count <= after + 1);
    }
}
