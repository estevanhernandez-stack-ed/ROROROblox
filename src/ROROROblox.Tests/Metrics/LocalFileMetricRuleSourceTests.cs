using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Metrics;
using ROROROblox.Core.Metrics;
using ROROROblox.Tests;

namespace ROROROblox.Tests.Metrics;

public class LocalFileMetricRuleSourceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rororo-rules-{Guid.NewGuid():N}.json");

    private LocalFileMetricRuleSource Sut(ILogger<LocalFileMetricRuleSource>? logger = null) =>
        new(_path, logger ?? NullLogger<LocalFileMetricRuleSource>.Instance);

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
        // CurrentRules() caches its parse, but keys the cache on a hash of the file's bytes: an
        // edit changes the hash, so it is still picked up without a restart. Only an unchanged
        // file — same content, same hash — skips the reparse (see AnUnchangedFile_IsNotReparsedOnEveryCall).
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
    public void TheCachedRules_CannotBeMutatedByACaller()
    {
        // The cache hands out the same instance on every hit (see
        // AnUnchangedFile_IsNotReparsedOnEveryCall). If that instance were a plain List<T>
        // reachable through a cast, a caller mutating it would corrupt what every later call
        // sees until the file next changes. AsReadOnly() wraps it in a distinct concrete type
        // whose mutating members throw instead.
        Write("""[ { "metricId": "a", "kind": "Event" } ]""");
        var rules = Sut().CurrentRules();

        var asMutable = Assert.IsAssignableFrom<IList<MetricRule>>(rules);
        Assert.Throws<NotSupportedException>(() => asMutable.Add(rules[0]));
        Assert.Throws<NotSupportedException>(() => asMutable.Clear());
    }

    [Fact]
    public void AMalformedFile_LogsOnceAndNotOncePerCall()
    {
        // Defect 2 was two halves: no cache, and a malformed file's log line repeating once per
        // reported metric. The cache fixes both, but only this test pins the log-spam half —
        // nothing else in this file calls CurrentRules() twice against the same bad content.
        var logger = new CapturingLogger<LocalFileMetricRuleSource>();
        Write("[ { \"metricId\": \"a\", ");
        var sut = Sut(logger);

        Assert.Empty(sut.CurrentRules());
        Assert.Empty(sut.CurrentRules());

        Assert.Single(logger.Snapshot());
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch (Exception) { /* temp file */ }
    }
}
