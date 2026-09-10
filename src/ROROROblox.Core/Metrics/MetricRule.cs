namespace ROROROblox.Core.Metrics;

/// <summary>
/// How a metric is judged. Three kinds, because "points per minute" is too narrow: a tracked
/// number can be a climbing counter, a standing level, or a thing that simply changed.
/// </summary>
public enum MetricRuleKind
{
    /// <summary>The value climbs. Breach when its change per minute over the window falls
    /// below the threshold. An unmeasurable rate is never a breach.</summary>
    Rate,

    /// <summary>The value is absolute. Breach when it crosses the threshold in the stated
    /// direction. The window is unused.</summary>
    Level,

    /// <summary>Breach when the value differs from the previous observation. Threshold and
    /// direction are unused — this is a change detector.</summary>
    Event,
}

/// <summary>One rule against one metric. Supplied by the caller; core never invents one.</summary>
/// <param name="AlertWhenBelow">Level only. Ignored by Rate (always "below") and Event.</param>
public sealed record MetricRule(
    string MetricId,
    MetricRuleKind Kind,
    double Threshold,
    TimeSpan Window,
    bool AlertWhenBelow = true);

/// <summary>
/// The outcome. <paramref name="Reason"/> is a resx KEY fragment, never a sentence —
/// <c>CoreStringBoundaryFenceTests</c> bans prose in Core, and the App renders it through
/// <c>CoreMessageCatalog</c>.
/// </summary>
public sealed record MetricBreachVerdict(bool Breached, double? Observed, string? Reason);
