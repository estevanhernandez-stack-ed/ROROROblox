using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricAlertCoordinatorTests
{
    private static readonly Guid Acct = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string M = "battle.points";

    private static (MetricAlertCoordinator Sut, FakeTimeProvider Clock) New(params MetricRule[] rules)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricAlertCoordinator(clock);
        sut.SetRules(rules);
        return (sut, clock);
    }

    private static MetricObservation Obs(double v, DateTimeOffset at) => new(Acct, M, v, at);

    [Fact]
    public void NoRuleForTheMetric_ProducesNothing()
    {
        var (sut, clock) = New();
        Assert.Empty(sut.Observe(Obs(100, clock.GetUtcNow()), "Masked", "Real"));
    }

    [Fact]
    public void ARateBreach_ProducesOneMetricBreachTrigger()
    {
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));

        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));
        var triggers = sut.Observe(Obs(500, clock.GetUtcNow()), "Masked", "Real");   // 50/min

        var t = Assert.Single(triggers);
        Assert.Equal(AlertKind.MetricBreach, t.Kind);
        Assert.Equal(Acct, t.AccountId);
        Assert.Equal("Masked", t.DisplayName);
        Assert.Equal("Real", t.RealName);
    }

    [Fact]
    public void HealthyRate_ProducesNothing()
    {
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));

        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Empty(sut.Observe(Obs(2000, clock.GetUtcNow()), "Masked", "Real"));
    }

    [Fact]
    public void FirstObservation_NeverAlerts()
    {
        // A cold start has no rate. This is the startup-page bug, guarded at the seam as well as
        // in the evaluator, because this is the surface a plugin actually touches.
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));
        Assert.Empty(sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real"));
    }

    [Fact]
    public void TheCoordinatorDoesNotDeduplicate_TheRouterDoes()
    {
        // Deliberate: repeated breaches produce repeated triggers here, and AlertRouter's
        // per-(account, kind) cooldown is what stops them becoming forty notifications. Keeping
        // suppression in ONE place is the point -- two half-implementations disagree eventually.
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));

        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(sut.Observe(Obs(500, clock.GetUtcNow()), "Masked", "Real"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Single(sut.Observe(Obs(550, clock.GetUtcNow()), "Masked", "Real"));
    }

    [Fact]
    public void TwoRulesOnOneMetricBothBreaching_StillProduceOneTrigger()
    {
        // Two rules sharing a MetricId is the designed case: the three rule kinds exist so one
        // number can be judged several ways. Both breaching at once used to emit two triggers for
        // one account, and AlertRouter — which groups per kind, as four shipped kinds need it to —
        // handed WebhookPayload a batch that read "2 accounts — battle.points" with the same alt
        // listed twice at two different values, in the clan channel, the one destination that uses
        // real names. The tie-break is the configured order: first breaching rule wins.
        var rate = new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10));
        var level = new MetricRule(M, MetricRuleKind.Level, 1000, TimeSpan.FromMinutes(10));

        var (sut, clock) = New(rate, level);
        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));
        // 500 points in 10 minutes: 50/min, under the rate floor of 100 — and 500 is under the
        // level floor of 1000. Both rules breach on this one observation.
        var t = Assert.Single(sut.Observe(Obs(500, clock.GetUtcNow()), "Masked", "Real"));
        Assert.Equal(50d, t.MetricValue);       // the rate rule, listed first, is the one that fired

        var (reversed, clock2) = New(level, rate);
        reversed.Observe(Obs(0, clock2.GetUtcNow()), "Masked", "Real");
        clock2.Advance(TimeSpan.FromMinutes(10));
        var t2 = Assert.Single(reversed.Observe(Obs(500, clock2.GetUtcNow()), "Masked", "Real"));
        Assert.Equal(500d, t2.MetricValue);     // same observation, order reversed — the level wins
    }

    [Fact]
    public void TriggerCarriesNoProse()
    {
        // Core emits data; the App renders the sentence. GameName is reused as the metric id
        // carrier rather than adding a field to a record four other kinds depend on.
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));
        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));

        var t = Assert.Single(sut.Observe(Obs(500, clock.GetUtcNow()), "Masked", "Real"));
        Assert.Equal(M, t.GameName);
    }
}
