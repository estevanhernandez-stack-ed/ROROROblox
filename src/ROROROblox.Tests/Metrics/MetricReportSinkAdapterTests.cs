using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.App.Plugins.Adapters;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class MetricReportSinkAdapterTests
{
    private static readonly Guid Acct = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string M = "battle.points";

    private sealed class FixedRules(params MetricRule[] rules) : IMetricRuleSource
    {
        public IReadOnlyList<MetricRule> CurrentRules() => rules;
    }

    /// <summary>Counts calls to <see cref="CurrentRules"/> so a test can prove the gate
    /// short-circuits BEFORE rules are read, not merely that nothing ended up raised — a report
    /// that reaches the coordinator and simply produces zero triggers would pass an
    /// emptiness-only assertion just as well as a true short-circuit, and that gap is exactly the
    /// contamination bug this adapter exists to prevent.</summary>
    private sealed class CountingRules(params MetricRule[] rules) : IMetricRuleSource
    {
        public int CallCount { get; private set; }

        public IReadOnlyList<MetricRule> CurrentRules()
        {
            CallCount++;
            return rules;
        }
    }

    private static (MetricReportSinkAdapter Sut, FakeTimeProvider Clock, List<AlertTrigger> Raised) New(
        bool enabled = true, IMetricRuleSource? rules = null)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricReportSinkAdapter(
            rules ?? new FixedRules(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10))),
            () => enabled,
            _ => ("Masked", "Real"),
            clock,
            NullLogger<MetricReportSinkAdapter>.Instance);

        var raised = new List<AlertTrigger>();
        sut.AlertsRaised += (_, t) => raised.AddRange(t);
        return (sut, clock, raised);
    }

    private static long Ms(DateTimeOffset at) => at.ToUnixTimeMilliseconds();

    [Fact]
    public void ABreach_RaisesATrigger_CarryingBothNames()
    {
        var (sut, clock, raised) = New();

        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(10));
        sut.Report(Acct.ToString(), M, 500, Ms(clock.GetUtcNow()));   // 50/min, floor is 100

        var t = Assert.Single(raised);
        Assert.Equal(AlertKind.MetricBreach, t.Kind);
        Assert.Equal(Acct, t.AccountId);
        Assert.Equal("Masked", t.DisplayName);
        Assert.Equal("Real", t.RealName);
    }

    [Fact]
    public void WhenTheSettingIsOff_NothingIsEvenRecorded()
    {
        // Spec section 6, requirement 1. Through plan 1 this feature was off only because the
        // destination list happened to be empty. If the gate is not HERE, the toggle is decorative.
        //
        // Emptiness alone would pass just as well against a gate placed AFTER the coordinator
        // observes — the contamination bug this test's name is about, since a report that still
        // reaches MetricHistory but happens to produce zero triggers looks identical to a report
        // the gate stopped. CountingRules proves the short-circuit directly: the gate returns
        // before rules are ever read, so CurrentRules() must never be called while the setting is
        // off.
        var rules = new CountingRules(new MetricRule(M, MetricRuleKind.Rate, 100, TimeSpan.FromMinutes(10)));
        var (sut, clock, raised) = New(enabled: false, rules: rules);

        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(10));
        sut.Report(Acct.ToString(), M, 500, Ms(clock.GetUtcNow()));

        Assert.Empty(raised);
        Assert.Equal(0, rules.CallCount);
    }

    [Fact]
    public void TurningTheGateOn_TakesEffectOnTheNextReport()
    {
        // Read per report, not cached at construction: a user who flips the switch must not have
        // to restart the app to be believed.
        var enabled = false;
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricReportSinkAdapter(
            new FixedRules(new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero)),
            () => enabled,
            _ => ("Masked", "Real"),
            clock,
            NullLogger<MetricReportSinkAdapter>.Instance);

        var raised = new List<AlertTrigger>();
        sut.AlertsRaised += (_, t) => raised.AddRange(t);

        sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow()));
        Assert.Empty(raised);

        enabled = true;
        sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow()));
        Assert.Single(raised);
    }

    [Fact]
    public void AFutureDatedReport_IsDropped_NotClamped()
    {
        // Spec section 6, requirement 2. Clamping would invent data: it places a foreign clock's
        // reading at our now and computes a rate over an interval that never happened.
        var (sut, clock, raised) = New();

        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(10));
        sut.Report(Acct.ToString(), M, 500, Ms(clock.GetUtcNow().AddHours(2)));

        // Had it been clamped to now, this would have been a 50/min breach.
        Assert.Empty(raised);
    }

    [Fact]
    public void ASlightlyFutureReport_IsAccepted()
    {
        // A hard "any future instant is a lie" test would reject every report from a machine
        // whose clock is a second fast, which is most machines. The tolerance is what keeps this
        // a skew detector rather than a clock-sync requirement.
        var (sut, clock, raised) = New();

        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(10));
        sut.Report(Acct.ToString(), M, 500, Ms(clock.GetUtcNow().AddSeconds(2)));

        Assert.Single(raised);
    }

    [Fact]
    public void AnUnparseableSubject_BecomesTheGlobalCarrier()
    {
        // MetricObservation's documented convention, shared with AlertKind.UptimeMark: an
        // observation belonging to no account uses Guid.Empty. A plugin that guesses an id wrong
        // should still reach the user rather than have the alert vanish.
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricReportSinkAdapter(
            new FixedRules(new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero)),
            () => true,
            _ => ("Masked", "Real"),
            clock,
            NullLogger<MetricReportSinkAdapter>.Instance);

        var raised = new List<AlertTrigger>();
        sut.AlertsRaised += (_, t) => raised.AddRange(t);

        sut.Report("not-a-guid", M, 400, Ms(clock.GetUtcNow()));

        Assert.Equal(Guid.Empty, Assert.Single(raised).AccountId);
    }

    [Fact]
    public void NoRules_MeansNoWork_AndNoAlert()
    {
        // One SUT. Subscribing to one adapter and reporting to another would make Assert.Empty
        // pass no matter what the code did.
        var (sut, clock, raised) = New(rules: new FixedRules());

        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromMinutes(10));
        sut.Report(Acct.ToString(), M, 0, Ms(clock.GetUtcNow()));   // 0/min would breach any floor

        Assert.Empty(raised);
    }

    [Fact]
    public void ASubscriberThatThrows_DoesNotThrowIntoTheReporter()
    {
        // This runs on a gRPC handler thread. Nothing here may take down the plugin host, and an
        // alert is a passenger -- the same contract AlertDispatcher and MainViewModel.RaiseAlerts
        // already hold.
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricReportSinkAdapter(
            new FixedRules(new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero)),
            () => true,
            _ => ("Masked", "Real"),
            clock,
            NullLogger<MetricReportSinkAdapter>.Instance);

        sut.AlertsRaised += (_, _) => throw new InvalidOperationException("subscriber blew up");

        var ex = Record.Exception(() => sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow())));
        Assert.Null(ex);
    }
}
