namespace ROROROblox.Core.Metrics;

/// <summary>Applies one <see cref="MetricRule"/> to one account's history. Pure and stateless.</summary>
public static class MetricEvaluator
{
    private static readonly MetricBreachVerdict NoBreach = new(false, null, null);

    public static MetricBreachVerdict Evaluate(
        MetricRule rule, MetricHistory history, Guid accountId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(history);

        return rule.Kind switch
        {
            MetricRuleKind.Rate => EvaluateRate(rule, history, accountId, nowUtc),
            MetricRuleKind.Level => EvaluateLevel(rule, history, accountId),
            MetricRuleKind.Event => EvaluateEvent(rule, history, accountId),
            _ => NoBreach,
        };
    }

    private static MetricBreachVerdict EvaluateRate(
        MetricRule rule, MetricHistory history, Guid accountId, DateTimeOffset nowUtc)
    {
        var rate = history.RatePerMinute(accountId, rule.MetricId, rule.Window, nowUtc);
        // Unknown is NOT zero. Zero is the alert condition, so treating an unmeasurable rate as
        // zero pages every user at startup and after every counter reset.
        if (rate is null) return NoBreach;
        return rate.Value < rule.Threshold
            ? new MetricBreachVerdict(true, rate.Value, "rate_below")
            : new MetricBreachVerdict(false, rate.Value, null);
    }

    private static MetricBreachVerdict EvaluateLevel(MetricRule rule, MetricHistory history, Guid accountId)
    {
        var latest = history.Latest(accountId, rule.MetricId);
        if (latest is null) return NoBreach;
        var breached = rule.AlertWhenBelow ? latest.Value < rule.Threshold : latest.Value > rule.Threshold;
        return breached
            ? new MetricBreachVerdict(true, latest.Value, rule.AlertWhenBelow ? "level_below" : "level_above")
            : new MetricBreachVerdict(false, latest.Value, null);
    }

    private static MetricBreachVerdict EvaluateEvent(MetricRule rule, MetricHistory history, Guid accountId)
    {
        if (history.Count(accountId, rule.MetricId) < 2) return NoBreach;
        var changed = history.Changed(accountId, rule.MetricId);
        var latest = history.Latest(accountId, rule.MetricId);
        return changed
            ? new MetricBreachVerdict(true, latest, "value_changed")
            : new MetricBreachVerdict(false, latest, null);
    }
}
