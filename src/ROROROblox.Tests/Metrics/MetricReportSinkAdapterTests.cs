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
        Assert.Empty(raised);                        // held for the grouping window
        clock.Advance(MetricBreachBatcher.Window);

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

        clock.Advance(MetricBreachBatcher.Window);
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
        clock.Advance(MetricBreachBatcher.Window);
        Assert.Empty(raised);

        // A report made while the gate is shut is not recorded at all, so switching the gate on
        // starts the series from nothing — and a rule fires on the CROSSING (2026-09-20), which
        // needs a previous state to cross from. So the first reading after the switch is the
        // baseline and the second is the one that can alert. That is a real few minutes of
        // quiet after someone turns metric alerts on, and it is the honest behaviour: the app
        // has not yet seen this number do anything.
        enabled = true;
        sut.Report(Acct.ToString(), M, 600, Ms(clock.GetUtcNow()));
        clock.Advance(MetricBreachBatcher.Window);
        Assert.Empty(raised);

        sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow()));
        clock.Advance(MetricBreachBatcher.Window);
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

        clock.Advance(MetricBreachBatcher.Window);
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

        clock.Advance(MetricBreachBatcher.Window);
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

        // Two reports, because a rule fires on the CROSSING (2026-09-20): 600 is above the floor,
        // 400 is under it. Both land on the same series, which is the thing being proved — an
        // unparseable subject maps to one carrier rather than a new series per bad id.
        sut.Report("not-a-guid", M, 600, Ms(clock.GetUtcNow()));
        clock.Advance(TimeSpan.FromSeconds(30));
        sut.Report("not-a-guid", M, 400, Ms(clock.GetUtcNow()));

        clock.Advance(MetricBreachBatcher.Window);
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

        clock.Advance(MetricBreachBatcher.Window);
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

        // Since 2026-09-15 the subscriber runs when the grouping window closes, on the timer, so
        // both the report and the flush must stay quiet.
        var ex = Record.Exception(() =>
        {
            sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow()));
            clock.Advance(MetricBreachBatcher.Window);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void EightAccountsInOneRead_AreOneAlertOfEight()
    {
        // The live test on 2026-09-15: this rule, eight accounts in one Ur Score read ~100 ms apart,
        // and 24 notifications — one per account per destination.
        var rule = new MetricRule("ps99.diamonds", MetricRuleKind.Level, 0, TimeSpan.Zero,
            AlertWhenBelow: false, Label: "Diamonds");
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new MetricReportSinkAdapter(
            new FixedRules(rule), () => true, _ => ("Masked", "Real"), clock,
            NullLogger<MetricReportSinkAdapter>.Instance);
        var batches = new List<IReadOnlyList<AlertTrigger>>();
        sut.AlertsRaised += (_, t) => batches.Add(t);

        // A baseline read for all eight, at zero, which is not above the floor of zero. Then the
        // read that crosses. A rule fires on the CROSSING (2026-09-20), and this test is about
        // what the BATCHER does with eight of them at once, which is unchanged.
        var accounts = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid().ToString()).ToList();
        foreach (var account in accounts)
        {
            sut.Report(account, "ps99.diamonds", 0, Ms(clock.GetUtcNow()));
            clock.Advance(TimeSpan.FromMilliseconds(12));
        }
        clock.Advance(MetricBreachBatcher.Window);
        Assert.Empty(batches);

        for (var i = 0; i < accounts.Count; i++)
        {
            sut.Report(accounts[i], "ps99.diamonds", 2_974_993 + i, Ms(clock.GetUtcNow()));
            clock.Advance(TimeSpan.FromMilliseconds(12));
        }
        Assert.Empty(batches);

        clock.Advance(MetricBreachBatcher.Window);

        var batch = Assert.Single(batches);
        Assert.Equal(8, batch.Count);
        Assert.All(batch, t => Assert.Equal(rule, t.Rule));
    }

    [Fact]
    public void DisposingTheSink_DropsAGroupStillInsideTheWindow()
    {
        // The container disposes the sink on exit; its timers must not outlive it.
        var (sut, clock, raised) = New(rules: new FixedRules(new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero)));

        sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow()));
        sut.Dispose();
        clock.Advance(MetricBreachBatcher.Window);

        Assert.Empty(raised);
    }

    [Fact]
    public void AReportAfterTheSinkIsDisposed_RaisesNothing_AndASecondDisposeIsHarmless()
    {
        // App.OnExit disposes the sink right after the plugin host stops, and the container disposes
        // it again at the very end (2026-09-15). A handler still draining when the host's 2 s stop
        // gives up can report into a disposed sink; that must neither throw nor open a new group.
        var (sut, clock, raised) = New(rules: new FixedRules(new MetricRule(M, MetricRuleKind.Level, 500, TimeSpan.Zero)));

        sut.Dispose();
        sut.Report(Acct.ToString(), M, 400, Ms(clock.GetUtcNow()));
        clock.Advance(MetricBreachBatcher.Window);
        sut.Dispose();

        Assert.Empty(raised);
    }
}

/// <summary>
/// App.OnExit is not constructible in a test (it is a WPF <c>Application</c> override), so this
/// fence reads the source the way the repo's other composition fences do. It pins the ordering that
/// makes "pending groups are dropped on exit" true: the metric sink is disposed right after the
/// plugin host stops (nothing new can arrive) and before the rest of the teardown, rather than only
/// by the container's DisposeAsync at the very end, when a group opened in the last five seconds
/// could still send while the tray and HTTP clients were going away (final review, 2026-09-15).
/// </summary>
public class MetricSinkExitOrderFenceTests
{
    [Fact]
    public void OnExit_DisposesTheMetricSink_RightAfterThePluginHostStops()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate the repo root from the test bin directory.");
        var source = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.App", "App.xaml.cs"));

        var onExit = source.IndexOf("protected override void OnExit(", StringComparison.Ordinal);
        Assert.True(onExit >= 0, "OnExit is gone from App.xaml.cs — update this fence with its new home.");
        var body = source[onExit..];

        var hostStop = body.IndexOf("pluginHost.StopAsync(", StringComparison.Ordinal);
        var sinkDispose = body.IndexOf("IMetricReportSink>() as IDisposable)?.Dispose()", StringComparison.Ordinal);
        var presenceStop = body.IndexOf("presence?.Stop()", StringComparison.Ordinal);
        var containerDispose = body.IndexOf("_services.DisposeAsync()", StringComparison.Ordinal);

        Assert.True(hostStop >= 0, "The plugin host stop is gone from OnExit.");
        Assert.True(sinkDispose >= 0, "OnExit no longer disposes the metric sink explicitly.");
        Assert.True(presenceStop >= 0 && containerDispose >= 0, "OnExit's later teardown steps moved; re-anchor this fence.");
        Assert.True(hostStop < sinkDispose, "The metric sink must be disposed AFTER the plugin host stops.");
        Assert.True(sinkDispose < presenceStop, "The metric sink must be disposed right after the plugin host, before the rest of the teardown.");
        Assert.True(sinkDispose < containerDispose);
    }
}
