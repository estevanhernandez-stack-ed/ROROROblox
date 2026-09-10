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
        Assert.True(MetricEvaluator.Evaluate(rule, Seeded((0, 400)), Acct, T(0)).Breached);
        Assert.False(MetricEvaluator.Evaluate(rule, Seeded((0, 600)), Acct, T(0)).Breached);
    }

    [Fact]
    public void Level_AboveThreshold_Breaches_WhenAlertWhenAbove()
    {
        var rule = new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero, AlertWhenBelow: false);
        Assert.True(MetricEvaluator.Evaluate(rule, Seeded((0, 600)), Acct, T(0)).Breached);
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
