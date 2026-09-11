namespace ROROROblox.Core.Metrics;

/// <summary>
/// Where the rules come from. One method, deliberately: callers re-read on every observation
/// rather than subscribing to a change event, which is cheap for a handful of rules and removes
/// the stale-rules failure mode entirely.
///
/// <para>
/// This interface exists so the SOURCE can change without the consumer changing — swapping the
/// registration in <c>App.xaml.cs</c> is the whole cost. The one implementation,
/// <c>LocalFileMetricRuleSource</c>, reads a local JSON file the user writes, and is the permanent
/// source: the signed manifest once planned to swap in here was dropped 2026-09-11, once the
/// plugin/core split left its payloads nothing to do. The seam still earns its keep even though
/// that swap will not happen — it is why a future rule source, should one ever be needed, costs
/// one registration and not a consumer rewrite.
/// </para>
/// </summary>
public interface IMetricRuleSource
{
    /// <summary>The rules as they stand right now. Empty is the shipped default and is never
    /// an error — this feature is opt-in and a user with no rules must see nothing happen.</summary>
    IReadOnlyList<MetricRule> CurrentRules();
}
