namespace ROROROblox.Core.Metrics;

/// <summary>
/// Applies one <see cref="MetricRule"/> to one account's history. Pure and stateless.
///
/// <para>
/// EVERY KIND FIRES ON THE CROSSING, NEVER ON THE STATE. A rule is breached when it is true NOW
/// and was not true one sample ago. Level and Rate used to answer "is it true?", which is a
/// different question: a condition that stays true was a fresh breach on every single observation,
/// and the only brake was <c>AlertRouter</c>'s five-minute cooldown. With a reporter on a
/// three-minute timer that is a notification roughly every six minutes, for hours, on every phone
/// the rule was set on. Nobody would keep the app installed. Found by review before a clan of
/// forty was asked to set these rules (2026-09-20). <see cref="MetricRuleKind.Event"/> already
/// worked this way, by construction.
/// </para>
/// <para>
/// A series with one sample has no previous state, so it has no crossing and says nothing. That is
/// also what happens after RoRoRo restarts, since the coordinator's history lives in memory: a
/// condition that was already true is not re-announced on startup, and is announced the next time
/// it genuinely crosses. The alternative — treating "no previous state" as "was fine" — would page
/// everyone whose condition was still true every time the app started.
/// </para>
/// </summary>
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
        if (rate.Value >= rule.Threshold) return new MetricBreachVerdict(false, rate.Value, null);

        // The same window, ending one sample back. An unmeasurable rate THEN counts as not
        // breached, which is right: a rule that could not be judged a moment ago and is breached
        // now has genuinely just crossed.
        if (history.Previous(accountId, rule.MetricId) is not { } previous) return NoBreach;
        var before = history.RatePerMinute(accountId, rule.MetricId, rule.Window, previous.AtUtc);
        if (before is not null && before.Value < rule.Threshold) return new MetricBreachVerdict(false, rate.Value, null);

        return new MetricBreachVerdict(true, rate.Value, "rate_below");
    }

    private static MetricBreachVerdict EvaluateLevel(MetricRule rule, MetricHistory history, Guid accountId)
    {
        var latest = history.Latest(accountId, rule.MetricId);
        if (latest is null) return NoBreach;

        var breached = Crosses(rule, latest.Value);
        if (!breached) return new MetricBreachVerdict(false, latest.Value, null);

        if (history.Previous(accountId, rule.MetricId) is not { } previous) return NoBreach;
        if (Crosses(rule, previous.Value)) return new MetricBreachVerdict(false, latest.Value, null);

        return new MetricBreachVerdict(true, latest.Value, rule.AlertWhenBelow ? "level_below" : "level_above");
    }

    private static bool Crosses(MetricRule rule, double value) =>
        rule.AlertWhenBelow ? value < rule.Threshold : value > rule.Threshold;

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
