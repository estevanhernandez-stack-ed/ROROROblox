using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricEvaluatorTests
{
    private static readonly Guid Acct = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string M = "battle.points";
    private static DateTimeOffset T(int minutes) => DateTimeOffset.UnixEpoch.AddMinutes(minutes);

    private static MetricHistory Seeded(params (int Minute, double Value)[] s)
    {
        var h = new MetricHistory();
        foreach (var (m, v) in s) h.Add(new MetricObservation(Acct, M, v, T(m)));
        return h;
    }

    private static MetricRule Rate(double floor) =>
        new(M, MetricRuleKind.Rate, floor, TimeSpan.FromMinutes(10));

    [Fact]
    public void Rate_BelowTheFloor_Breaches()
    {
        var v = MetricEvaluator.Evaluate(Rate(100), Seeded((0, 0), (10, 500)), Acct, T(10));
        Assert.True(v.Breached);
        Assert.Equal(50d, v.Observed!.Value, 3);
    }

    [Fact]
    public void Rate_AtOrAboveTheFloor_DoesNot()
    {
        Assert.False(MetricEvaluator.Evaluate(Rate(100), Seeded((0, 0), (10, 1000)), Acct, T(10)).Breached);
        Assert.False(MetricEvaluator.Evaluate(Rate(100), Seeded((0, 0), (10, 2000)), Acct, T(10)).Breached);
    }

    [Fact]
    public void Rate_UnknownIsNeverABreach()
    {
        // The single most important line in this file. Unknown is not zero, and zero is the
        // alert condition -- conflating them pages every user on startup and after every reset.
        var oneSample = MetricEvaluator.Evaluate(Rate(100), Seeded((0, 0)), Acct, T(1));
        Assert.False(oneSample.Breached);
        Assert.Null(oneSample.Observed);

        var afterReset = MetricEvaluator.Evaluate(Rate(100), Seeded((0, 9000), (10, 0)), Acct, T(10));
        Assert.False(afterReset.Breached);
    }

    [Fact]
    public void Level_BelowThreshold_Breaches_WhenAlertWhenBelow()
    {
        var rule = new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero, AlertWhenBelow: true);
        Assert.True(MetricEvaluator.Evaluate(rule, Seeded((0, 600), (5, 400)), Acct, T(5)).Breached);
        Assert.False(MetricEvaluator.Evaluate(rule, Seeded((0, 400), (5, 600)), Acct, T(5)).Breached);
    }

    [Fact]
    public void Level_AboveThreshold_Breaches_WhenAlertWhenAbove()
    {
        var rule = new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero, AlertWhenBelow: false);
        Assert.True(MetricEvaluator.Evaluate(rule, Seeded((0, 400), (5, 600)), Acct, T(5)).Breached);
        Assert.False(MetricEvaluator.Evaluate(rule, Seeded((0, 600), (5, 400)), Acct, T(5)).Breached);
    }

    /// <summary>
    /// The whole point of the crossing rule. A condition that stays true is announced ONCE. Before
    /// 2026-09-20 every one of these observations was a fresh breach, and with a reporter on a
    /// three-minute timer and a five-minute cooldown that is a notification roughly every six
    /// minutes, for hours, on every phone the rule was set on.
    /// </summary>
    [Fact]
    public void Level_StayingBreached_IsAnnouncedOnce()
    {
        var rule = new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero, AlertWhenBelow: true);
        var h = Seeded((0, 600));

        h.Add(new MetricObservation(Acct, M, 400, T(5)));
        Assert.True(MetricEvaluator.Evaluate(rule, h, Acct, T(5)).Breached);

        foreach (var minute in new[] { 10, 15, 20, 25 })
        {
            h.Add(new MetricObservation(Acct, M, 300, T(minute)));
            Assert.False(MetricEvaluator.Evaluate(rule, h, Acct, T(minute)).Breached);
        }

        // Back over the line, then under it again: a second genuine crossing, a second alert.
        h.Add(new MetricObservation(Acct, M, 700, T(30)));
        Assert.False(MetricEvaluator.Evaluate(rule, h, Acct, T(30)).Breached);
        h.Add(new MetricObservation(Acct, M, 450, T(35)));
        Assert.True(MetricEvaluator.Evaluate(rule, h, Acct, T(35)).Breached);
    }

    /// <summary>
    /// A rate that stays under the floor is one alert too. The previous state is measured over the
    /// same window ending one sample back, not over the whole series.
    /// </summary>
    [Fact]
    public void Rate_StayingUnderTheFloor_IsAnnouncedOnce()
    {
        var rule = Rate(100);
        var h = Seeded((0, 0), (10, 2000));
        Assert.False(MetricEvaluator.Evaluate(rule, h, Acct, T(10)).Breached);

        // Falls off: 2000 -> 2100 over ten minutes is 10 a minute, under the floor of 100.
        h.Add(new MetricObservation(Acct, M, 2100, T(20)));
        Assert.True(MetricEvaluator.Evaluate(rule, h, Acct, T(20)).Breached);

        h.Add(new MetricObservation(Acct, M, 2150, T(30)));
        Assert.False(MetricEvaluator.Evaluate(rule, h, Acct, T(30)).Breached);

        h.Add(new MetricObservation(Acct, M, 2200, T(40)));
        Assert.False(MetricEvaluator.Evaluate(rule, h, Acct, T(40)).Breached);
    }

    /// <summary>
    /// One sample is no crossing. This is also what a RoRoRo restart looks like, since the
    /// coordinator's history is in memory: a condition that was already true is not re-announced
    /// on startup. The alternative pages everyone whose condition is still true, every launch.
    /// </summary>
    [Fact]
    public void Level_WithOnlyOneSample_HasNoCrossingToAnnounce()
    {
        var rule = new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero, AlertWhenBelow: true);

        Assert.False(MetricEvaluator.Evaluate(rule, Seeded((0, 400)), Acct, T(0)).Breached);
    }

    [Fact]
    public void Level_WithNoSamples_DoesNotBreach()
    {
        var rule = new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero);
        Assert.False(MetricEvaluator.Evaluate(rule, new MetricHistory(), Acct, T(0)).Breached);
    }

    [Fact]
    public void Event_BreachesOnChange_NotOnRepeat()
    {
        var rule = new MetricRule(M, MetricRuleKind.Event, 0, TimeSpan.Zero);
        Assert.True(MetricEvaluator.Evaluate(rule, Seeded((0, 3), (5, 4)), Acct, T(5)).Breached);
        Assert.False(MetricEvaluator.Evaluate(rule, Seeded((0, 3), (5, 3)), Acct, T(5)).Breached);
    }

    [Fact]
    public void Event_WithOneSample_DoesNotBreach()
    {
        var rule = new MetricRule(M, MetricRuleKind.Event, 0, TimeSpan.Zero);
        Assert.False(MetricEvaluator.Evaluate(rule, Seeded((0, 3)), Acct, T(0)).Breached);
    }

    [Fact]
    public void Verdict_CarriesAReasonKeyNotProse()
    {
        // CoreStringBoundaryFenceTests bans prose in Core. Reason is a KEY the App renders.
        var v = MetricEvaluator.Evaluate(Rate(100), Seeded((0, 0), (10, 500)), Acct, T(10));
        Assert.Equal("rate_below", v.Reason);
    }
}
