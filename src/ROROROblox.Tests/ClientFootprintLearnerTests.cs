using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// F-083. The advisor divides free memory by a constant measured on one machine; this is what
/// replaces it, and every rule below exists because getting it wrong has a specific failure mode.
/// </summary>
public class ClientFootprintLearnerTests
{
    private const long Mb = 1024L * 1024;

    private static ClientFootprintLearner Fed(int mb, int times, bool belowReserve = false)
    {
        var learner = new ClientFootprintLearner();
        for (var i = 0; i < times; i++) learner.Observe(mb * Mb, belowReserve);
        return learner;
    }

    [Fact]
    public void TheSeedHoldsUntilThereIsEnoughEvidence()
    {
        // Nineteen readings of a much cheaper client must not move the advisor. A confident number
        // from thin evidence is the failure this whole feature is meant to remove, not add.
        var learner = Fed(1500, ClientFootprintLearner.MinimumSamples - 1);

        Assert.Equal(ClientFootprintLearner.SeedMb, learner.EstimateMb);
    }

    [Fact]
    public void TheTwentiethReadingIsWhereItStartsTrustingItself()
    {
        var learner = Fed(1500, ClientFootprintLearner.MinimumSamples);

        Assert.Equal(1500, learner.EstimateMb);
    }

    [Fact]
    public void ItLearnsTheExpensiveEndRatherThanTheMiddle()
    {
        // p75, not median, and the difference is the whole point: the advisor answers "will another
        // one fit", so it should lean toward what this machine actually spends. A median would
        // promise room for a client that costs more than half of them do.
        // 12 cheap, 8 expensive. The MEDIAN of that is 2000 and the p75 is 3000, so the two
        // answers genuinely differ here — which is the only way this test says anything. An earlier
        // version used 15/5 and expected 3000, which is simply wrong: a quarter of the samples
        // being expensive puts p75 exactly at the boundary, and the arithmetic said so.
        var learner = new ClientFootprintLearner();
        for (var i = 0; i < 20; i++) learner.Observe((i < 12 ? 2000 : 3000) * Mb, belowReserve: false);

        Assert.Equal(3000, learner.EstimateMb);
    }

    [Fact]
    public void SamplesTakenUnderPressureAreDiscarded()
    {
        // A client below the reserve is being squeezed by the OS, so its footprint describes the
        // pressure and not the client. Learning from it would teach the advisor that clients are
        // cheap exactly when they are not — the moment its answer matters most.
        var learner = new ClientFootprintLearner();
        for (var i = 0; i < 50; i++) learner.Observe(1300 * Mb, belowReserve: true);

        Assert.Equal(0, learner.SampleCount);
        Assert.Equal(ClientFootprintLearner.SeedMb, learner.EstimateMb);
    }

    [Fact]
    public void ReadingsOutsideThePlausibleRangeAreNotClients()
    {
        // Below the floor is a crashed shell or a process mid-teardown; above the ceiling is an
        // outlier well past the 3280 MB peak ever measured. Neither is a footprint.
        var learner = new ClientFootprintLearner();
        for (var i = 0; i < 30; i++)
        {
            learner.Observe(400 * Mb, false);
            learner.Observe(9000 * Mb, false);
        }

        Assert.Equal(0, learner.SampleCount);
    }

    [Fact]
    public void AClientUpgradeThrowsAwayWhatWasLearnedAboutTheOldOne()
    {
        // A new build can move the footprint by hundreds of MB. An estimate averaged across an
        // upgrade describes a client that no longer exists.
        var learner = Fed(1500, 25);
        Assert.Equal(1500, learner.EstimateMb);

        learner.NoteClientVersion("733");   // first sighting only records
        Assert.Equal(1500, learner.EstimateMb);

        learner.NoteClientVersion("734");
        Assert.Equal(0, learner.SampleCount);
        Assert.Equal(ClientFootprintLearner.SeedMb, learner.EstimateMb);
    }

    [Fact]
    public void TheSameVersionSeenAgainChangesNothing()
    {
        var learner = Fed(1500, 25);
        learner.NoteClientVersion("733");
        learner.NoteClientVersion("733");
        learner.NoteClientVersion("733");

        Assert.Equal(1500, learner.EstimateMb);
    }

    [Fact]
    public void ItDoesNotGrowWithoutBound()
    {
        // Sampling every 30 seconds for days would otherwise accumulate forever, and an estimate
        // averaged over a week tracks nothing in particular.
        var learner = Fed(2000, 5000);

        Assert.InRange(learner.SampleCount, 1, 200);
        Assert.Equal(2000, learner.EstimateMb);
    }

    [Fact]
    public void TheEstimateStaysInsideTheClamp()
    {
        // Belt and braces on the range check: whatever the samples say, the number handed to the
        // advisor divides free memory, and a wild value there produces a wild promise.
        foreach (var mb in new[] { ClientFootprintLearner.FloorMb, ClientFootprintLearner.CeilingMb })
        {
            var learner = Fed(mb, 25);
            Assert.InRange(learner.EstimateMb, ClientFootprintLearner.FloorMb, ClientFootprintLearner.CeilingMb);
        }
    }
}
