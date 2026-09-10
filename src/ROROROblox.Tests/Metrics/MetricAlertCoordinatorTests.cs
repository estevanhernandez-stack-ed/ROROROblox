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
