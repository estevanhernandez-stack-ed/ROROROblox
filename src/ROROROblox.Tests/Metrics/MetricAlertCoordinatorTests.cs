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
    public void AConditionThatStaysTrue_IsOneTrigger_NotOnePerObservation()
    {
        // REVERSED 2026-09-20. This test used to assert the opposite, on the reasoning that
        // suppression belongs in ONE place and that place was AlertRouter's cooldown. That reading
        // conflated two different jobs. The cooldown is RATE LIMITING -- a backstop against a
        // flood, keyed on (account, metric id). Whether a rule that is still true is a NEW breach
        // is a question about the rule's meaning, and it belongs with the rule. It only looked
        // academic because the cooldown hid it at five-minute granularity: with a reporter on a
        // three-minute timer, "still true" meant a notification roughly every six minutes, for
        // hours, on every phone the rule was set on.
        //
        // The cooldown is untouched and still does its own job: two genuine crossings inside five
        // minutes are still collapsed by the router.
        var (sut, clock) = New(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));

        sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(sut.Observe(Obs(500, clock.GetUtcNow()), "Masked", "Real"));

        // Still under the floor a minute later, and a minute after that. The condition has not
        // changed, so there is nothing new to say about it.
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(sut.Observe(Obs(550, clock.GetUtcNow()), "Masked", "Real"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Empty(sut.Observe(Obs(600, clock.GetUtcNow()), "Masked", "Real"));
    }

    [Fact]
    public void TwoRulesOnOneMetric_ProduceOneTrigger_CarryingTheRuleThatFired()
    {
        // Two rules sharing a MetricId is the designed case: the three rule kinds exist so one
        // number can be judged several ways. Both breaching at once used to emit two triggers for
        // one account, and AlertRouter — which groups per kind, as four shipped kinds need it to —
        // handed WebhookPayload a batch that read "2 accounts — battle.points" with the same alt
        // listed twice at two different values, in the clan channel, the one destination that uses
        // real names. The tie-break is the configured order: first breaching rule wins.
        //
        // The trigger also carries that rule (2026-09-15): the alert's wording needs the label,
        // kind, threshold, window and direction of what fired, not just its metric id. "The rule
        // that fired" is not "the first rule for the metric", so the third case puts a rule that
        // stays quiet ahead of the one that breaches.
        var rate = new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10), Label: "Points");
        var level = new MetricRule(M, MetricRuleKind.Level, 100, TimeSpan.FromMinutes(10), AlertWhenBelow: false, Label: "Points");
        var quietLevel = new MetricRule(M, MetricRuleKind.Level, 1000, TimeSpan.Zero, AlertWhenBelow: false, Label: "Points");

        // 0 then, ten minutes later, 500. The rate is 50/min, under the floor of 100, and no rate
        // could be measured a sample earlier -- a crossing. The level goes from 0 (not above 100)
        // to 500 (above it) -- also a crossing, on the same observation, which is the case this
        // test is about. Neither value is above 1000, so the quiet one never breaches.
        //
        // Both rules have to CROSS here, not merely be true: a level of "below 1000" would have
        // been true at 0 as well as at 500, so under the crossing rule (2026-09-20) it would not
        // fire on the second observation at all and the tie-break would never be exercised.
        static AlertTrigger FireOnce(params MetricRule[] rules)
        {
            var (sut, clock) = New(rules);
            sut.Observe(Obs(0, clock.GetUtcNow()), "Masked", "Real");
            clock.Advance(TimeSpan.FromMinutes(10));
            return Assert.Single(sut.Observe(Obs(500, clock.GetUtcNow()), "Masked", "Real"));
        }

        var t = FireOnce(rate, level);
        Assert.Same(rate, t.Rule);              // the rate rule, listed first, is the one that fired
        Assert.Equal(50d, t.MetricValue);       // for Rate, the measured rate per minute
        Assert.Equal(M, t.GameName);            // the metric id stays where the harness looks for it

        var t2 = FireOnce(level, rate);
        Assert.Same(level, t2.Rule);            // same observation, order reversed — the level wins
        Assert.Equal(500d, t2.MetricValue);

        var t3 = FireOnce(quietLevel, rate);
        Assert.Same(rate, t3.Rule);             // the first rule for the metric did not breach; the second did
        Assert.Equal(50d, t3.MetricValue);
    }

    /// <summary>
    /// A recovery is its own kind, so it gets its own cooldown slot (AlertCooldownKey keys on kind)
    /// and cannot be swallowed by the breach that came moments before it.
    /// </summary>
    [Fact]
    public void ARecovery_IsItsOwnKindOfTrigger()
    {
        var rule = new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero, AlertWhenBelow: true,
            TellMeWhenItRecovers: true);
        var (sut, clock) = New(rule);

        sut.Observe(Obs(600, clock.GetUtcNow()), "Masked", "Real");
        clock.Advance(TimeSpan.FromMinutes(1));
        var down = Assert.Single(sut.Observe(Obs(400, clock.GetUtcNow()), "Masked", "Real"));
        Assert.Equal(AlertKind.MetricBreach, down.Kind);

        clock.Advance(TimeSpan.FromMinutes(1));
        var up = Assert.Single(sut.Observe(Obs(700, clock.GetUtcNow()), "Masked", "Real"));

        Assert.Equal(AlertKind.MetricRecovered, up.Kind);
        Assert.Equal(700d, up.MetricValue);
        Assert.Same(rule, up.Rule);
        Assert.NotEqual(AlertCooldownKey.For(down), AlertCooldownKey.For(up));
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
