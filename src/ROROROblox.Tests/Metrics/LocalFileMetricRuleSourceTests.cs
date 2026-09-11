using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Metrics;
using ROROROblox.Core.Metrics;

namespace ROROROblox.Tests.Metrics;

public class LocalFileMetricRuleSourceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rororo-rules-{Guid.NewGuid():N}.json");

    private LocalFileMetricRuleSource Sut() =>
        new(_path, NullLogger<LocalFileMetricRuleSource>.Instance);

    private void Write(string json) => File.WriteAllText(_path, json);

    [Fact]
    public void NoFile_MeansNoRules_NotAnError()
    {
        // The shipped default. Absent must be inert: this feature is opt-in, and a user who
        // never configured a rule must never see an exception, a warning, or an alert.
        Assert.Empty(Sut().CurrentRules());
    }

    [Fact]
    public void AWellFormedFile_ParsesEveryKind()
    {
        Write("""
        [
          { "metricId": "a", "kind": "Rate",  "threshold": 100, "windowMinutes": 10 },
          { "metricId": "b", "kind": "Level", "threshold": 500, "alertWhenBelow": false },
          { "metricId": "c", "kind": "Event" }
        ]
        """);

        var rules = Sut().CurrentRules();

        Assert.Equal(3, rules.Count);
        Assert.Equal(MetricRuleKind.Rate, rules[0].Kind);
        Assert.Equal(TimeSpan.FromMinutes(10), rules[0].Window);
        Assert.Equal(100, rules[0].Threshold);
        Assert.False(rules[1].AlertWhenBelow);
        Assert.Equal(MetricRuleKind.Event, rules[2].Kind);
    }

    [Fact]
    public void MalformedJson_YieldsNoRules_AndDoesNotThrow()
    {
        // A truncated file must not take the app down. This is read on the gRPC path, and
        // nothing on that path may throw into the plugin host.
        Write("[ { \"metricId\": \"a\", ");
        Assert.Empty(Sut().CurrentRules());
    }

    [Fact]
    public void AnUnknownKind_DropsThatRuleAndKeepsTheRest()
    {
        // One bad row must not cost the user the rules that were fine. Silently dropping the
        // whole file on one typo is how a person concludes the feature is broken.
        Write("""
        [
          { "metricId": "good", "kind": "Rate", "threshold": 1, "windowMinutes": 5 },
          { "metricId": "bad",  "kind": "Telepathy", "threshold": 1 }
        ]
        """);

        var rule = Assert.Single(Sut().CurrentRules());
        Assert.Equal("good", rule.MetricId);
    }

    [Fact]
    public void ARuleWithNoMetricId_IsDropped()
    {
        // A rule with no metric id matches nothing, so it is not a rule. Keeping it would put a
        // row in the file that looks configured and can never fire.
        Write("""[ { "kind": "Rate", "threshold": 1, "windowMinutes": 5 } ]""");
        Assert.Empty(Sut().CurrentRules());
    }

    [Fact]
    public void AnEditedFile_IsPickedUpWithoutRestart()
    {
        // CurrentRules() re-reads every call on purpose. It is a small file read on a path that
        // already crosses a named pipe, and it removes the entire stale-rules failure mode.
        var sut = Sut();
        Write("""[ { "metricId": "a", "kind": "Event" } ]""");
        Assert.Single(sut.CurrentRules());

        Write("""[ { "metricId": "a", "kind": "Event" }, { "metricId": "b", "kind": "Event" } ]""");
        Assert.Equal(2, sut.CurrentRules().Count);
    }

    [Fact]
    public void ARowWithAMistypedField_DropsOnlyThatRow()
    {
        // The whole point: a hand-edited file is where a stray quote happens, and losing every
        // rule because of one is indistinguishable from the feature being broken.
        Write("""
        [
          { "metricId": "good", "kind": "Rate", "threshold": 1, "windowMinutes": 5 },
          { "metricId": "bad",  "kind": "Rate", "threshold": "abc", "windowMinutes": 5 },
          { "metricId": "alsogood", "kind": "Event" }
        ]
        """);

        var rules = Sut().CurrentRules();

        Assert.Equal(2, rules.Count);
        Assert.DoesNotContain(rules, r => r.MetricId == "bad");
    }

    [Fact]
    public void ANullWhereANumberBelongs_DropsOnlyThatRow()
    {
        Write("""
        [
          { "metricId": "good", "kind": "Event" },
          { "metricId": "bad",  "kind": "Rate", "threshold": null, "windowMinutes": 5 }
        ]
        """);

        Assert.Equal("good", Assert.Single(Sut().CurrentRules()).MetricId);
    }

    [Fact]
    public void AnUnchangedFile_IsNotReparsedOnEveryCall()
    {
        // Called once per reported metric, on the gRPC path, for the life of the process.
        var sut = Sut();
        Write("""[ { "metricId": "a", "kind": "Event" } ]""");

        var first = sut.CurrentRules();
        var second = sut.CurrentRules();

        // Same instance, not merely equal: proves the parse was skipped, which an equality
        // assertion would not.
        Assert.Same(first, second);
    }

    [Fact]
    public void AnEditedFile_IsStillPickedUpWithoutRestart()
    {
        // The cache must not cost the live-reload behaviour an existing test already pins.
        var sut = Sut();
        Write("""[ { "metricId": "a", "kind": "Event" } ]""");
        Assert.Single(sut.CurrentRules());

        Write("""[ { "metricId": "a", "kind": "Event" }, { "metricId": "b", "kind": "Event" } ]""");
        Assert.Equal(2, sut.CurrentRules().Count);
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception) { /* temp file */ }
    }
}
