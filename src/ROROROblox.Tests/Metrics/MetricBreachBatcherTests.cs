using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricBreachBatcherTests
{
    private static readonly MetricRule Points =
        new("battle.points", MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10), Label: "Points");

    private static readonly MetricRule PointsLevel =
        new("battle.points", MetricRuleKind.Level, 1000, TimeSpan.Zero, Label: "Points");

    private static readonly MetricRule Diamonds =
        new("ps99.diamonds", MetricRuleKind.Level, 0, TimeSpan.Zero, AlertWhenBelow: false, Label: "Diamonds");

    private static AlertTrigger Breach(MetricRule rule, Guid account, double value) =>
        new(AlertKind.MetricBreach, account, "Masked", "Real", rule.MetricId, null, DateTimeOffset.UnixEpoch, value, rule);

    private static (MetricBreachBatcher Sut, FakeTimeProvider Clock, List<IReadOnlyList<AlertTrigger>> Flushed) New()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricBreachBatcher(clock, NullLogger.Instance);
        var flushed = new List<IReadOnlyList<AlertTrigger>>();
        sut.Flushed += (_, batch) => { lock (flushed) flushed.Add(batch); };
        return (sut, clock, flushed);
    }

    [Fact]
    public void NothingLeaves_UntilTheWindowCloses()
    {
        var (sut, clock, flushed) = New();

        sut.Add([Breach(Points, Guid.NewGuid(), 50)]);
        clock.Advance(MetricBreachBatcher.Window - TimeSpan.FromTicks(1));
        Assert.Empty(flushed);

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.Single(Assert.Single(flushed));
    }

    [Fact]
    public void TheWindowIsFixedFromTheFirstBreach_NotSliding()
    {
        // A steady trickle of breaches must not postpone the alert forever: the group closes
        // Window after its FIRST breach, and a breach after that starts a new group.
        var (sut, clock, flushed) = New();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();

        sut.Add([Breach(Points, a, 50)]);
        clock.Advance(MetricBreachBatcher.Window - TimeSpan.FromSeconds(1));
        sut.Add([Breach(Points, b, 40)]);
        clock.Advance(TimeSpan.FromSeconds(1));

        var first = Assert.Single(flushed);
        Assert.Equal(new[] { a, b }, first.Select(t => t.AccountId).ToArray());

        sut.Add([Breach(Points, c, 30)]);
        clock.Advance(MetricBreachBatcher.Window);

        Assert.Equal(2, flushed.Count);
        Assert.Equal(c, Assert.Single(flushed[1]).AccountId);
    }

    [Fact]
    public void DifferentStatsOrRules_StaySeparateAlerts()
    {
        // Same metric id, different rule, is a different alert too: "Points stopped climbing" and
        // "Points fell below 1,000" are different news.
        var (sut, clock, flushed) = New();

        sut.Add([Breach(Points, Guid.NewGuid(), 50)]);
        sut.Add([Breach(PointsLevel, Guid.NewGuid(), 500)]);
        sut.Add([Breach(Diamonds, Guid.NewGuid(), 2_974_993)]);
        sut.Add([Breach(Points, Guid.NewGuid(), 40)]);
        clock.Advance(MetricBreachBatcher.Window);

        Assert.Equal(3, flushed.Count);
        Assert.All(flushed, batch => Assert.Single(batch.Select(t => t.Rule).Distinct()));
        Assert.Equal(2, Assert.Single(flushed, batch => batch[0].Rule == Points).Count);
    }

    [Fact]
    public void TheSameAccountTwiceInOneWindow_IsListedOnce_WithItsLatestReading()
    {
        // Listing one alt twice at two values is the clan-channel defect the coordinator's
        // one-trigger-per-call rule exists to prevent; a window must not reintroduce it.
        var (sut, clock, flushed) = New();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        sut.Add([Breach(Points, a, 50)]);
        sut.Add([Breach(Points, b, 45)]);
        sut.Add([Breach(Points, a, 20)]);
        clock.Advance(MetricBreachBatcher.Window);

        var batch = Assert.Single(flushed);
        Assert.Equal(new[] { a, b }, batch.Select(t => t.AccountId).ToArray());
        Assert.Equal(20d, batch[0].MetricValue);
    }

    [Fact]
    public void ReportsFromManyThreadsAtOnce_AllLandInOneAlert()
    {
        // Reports arrive on gRPC handler threads, several at once during a plugin read.
        var (sut, clock, flushed) = New();
        var accounts = Enumerable.Range(0, 16).Select(_ => Guid.NewGuid()).ToArray();

        Parallel.ForEach(accounts, new ParallelOptions { MaxDegreeOfParallelism = 16 },
            account => sut.Add([Breach(Points, account, 1)]));
        clock.Advance(MetricBreachBatcher.Window);

        var batch = Assert.Single(flushed);
        Assert.Equal(accounts.Order().ToArray(), batch.Select(t => t.AccountId).Order().ToArray());
    }

    [Fact]
    public void ASubscriberThatThrows_StaysInsideTheTimer_AndTheNextGroupStillLeaves()
    {
        // An exception escaping a thread-pool timer callback ends the process. FakeTimeProvider runs
        // the callback inside Advance, so an uncaught throw would surface right here.
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricBreachBatcher(clock, NullLogger.Instance);
        var calls = 0;
        sut.Flushed += (_, _) =>
        {
            if (++calls == 1) throw new InvalidOperationException("subscriber blew up");
        };

        sut.Add([Breach(Points, Guid.NewGuid(), 1)]);
        Assert.Null(Record.Exception(() => clock.Advance(MetricBreachBatcher.Window)));

        sut.Add([Breach(Points, Guid.NewGuid(), 1)]);
        clock.Advance(MetricBreachBatcher.Window);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Dispose_DropsWhatIsPending_AndIgnoresAnythingAfter()
    {
        var (sut, clock, flushed) = New();

        sut.Add([Breach(Points, Guid.NewGuid(), 1)]);
        sut.Dispose();
        clock.Advance(MetricBreachBatcher.Window);

        sut.Add([Breach(Points, Guid.NewGuid(), 1)]);
        clock.Advance(MetricBreachBatcher.Window);

        Assert.Empty(flushed);
        sut.Dispose();   // twice is harmless: the container and a test may both call it
    }
}
