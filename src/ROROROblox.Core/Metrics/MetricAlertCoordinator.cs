using ROROROblox.Core.Discord;

namespace ROROROblox.Core.Metrics;

/// <summary>
/// The seam between "a number arrived" and "the alert system knows". Holds history, applies the
/// configured rules, and RETURNS triggers rather than sending them.
///
/// <para>
/// Returning is the whole design. The caller hands these to <c>AlertDispatcher</c>, which routes
/// through <c>AlertRouter</c> — so mute, per-(account, kind) cooldown, coalescing and
/// fallback-to-Local all apply exactly as they do for every other kind. A coordinator that
/// dispatched directly would bypass all four, and "a bad night becomes forty notifications" is
/// the one outcome this feature must not produce.
/// </para>
/// </summary>
public sealed class MetricAlertCoordinator(TimeProvider time, int historyCapacity = 64)
{
    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly MetricHistory _history = new(historyCapacity);
    private volatile IReadOnlyList<MetricRule> _rules = [];

    /// <summary>Replaces the rule set wholesale. Called on settings change and on manifest load.</summary>
    public void SetRules(IReadOnlyList<MetricRule> rules) =>
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));

    /// <summary>
    /// Records an observation and returns any triggers it caused. Empty is the common case and
    /// is not an error.
    /// </summary>
    /// <param name="displayName">Already streamer-masked by the caller, like every other trigger.</param>
    /// <param name="realName">The true account name. Only the clan destination is allowed to use it.</param>
    public IReadOnlyList<AlertTrigger> Observe(MetricObservation o, string displayName, string realName)
    {
        ArgumentNullException.ThrowIfNull(o);
        _history.Add(o);

        var now = _time.GetUtcNow();
        List<AlertTrigger>? triggers = null;

        foreach (var rule in _rules)
        {
            if (!string.Equals(rule.MetricId, o.MetricId, StringComparison.Ordinal)) continue;

            var verdict = MetricEvaluator.Evaluate(rule, _history, o.AccountId, now);
            if (!verdict.Breached) continue;

            // GameName carries the metric id rather than a fifth field on AlertTrigger: four
            // other kinds share that record, and widening it for one would touch every one of
            // them. PrivateBytes carries the observed value for the same reason.
            (triggers ??= []).Add(new AlertTrigger(
                AlertKind.MetricBreach,
                o.AccountId,
                displayName,
                realName,
                o.MetricId,
                verdict.Observed is null ? null : (long)verdict.Observed.Value,
                now));
        }

        return (IReadOnlyList<AlertTrigger>?)triggers ?? [];
    }
}
