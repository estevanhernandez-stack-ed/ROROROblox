namespace ROROROblox.Core.Metrics;

/// <summary>
/// Where the rules come from. One method, deliberately: callers re-read on every observation
/// rather than subscribing to a change event, which is cheap for a handful of rules and removes
/// the stale-rules failure mode entirely.
///
/// <para>
/// This interface exists so the SOURCE can change without the consumer changing. Plan 2 reads a
/// local JSON file the user writes; plan 3 reads a signed manifest. Swapping them is one
/// registration.
/// </para>
/// </summary>
public interface IMetricRuleSource
{
    /// <summary>The rules as they stand right now. Empty is the shipped default and is never
    /// an error — this feature is opt-in and a user with no rules must see nothing happen.</summary>
    IReadOnlyList<MetricRule> CurrentRules();
}
