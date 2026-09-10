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

    /// <summary>
    /// Replaces the rule set wholesale. Called on settings change and on manifest load.
    /// <para>
    /// The list is COPIED. An <c>IReadOnlyList</c> parameter is a promise the callee will not
    /// write to it, never a promise the caller will not — hand in a <c>List</c>, add to it later,
    /// and the uncopied version threw <c>InvalidOperationException</c> out of the
    /// <c>foreach</c> in <see cref="Observe"/> and up into whatever was reporting the number.
    /// </para>
    /// </summary>
    public void SetRules(IReadOnlyList<MetricRule> rules) =>
        _rules = rules is null ? throw new ArgumentNullException(nameof(rules)) : [.. rules];

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

        foreach (var rule in _rules)
        {
            if (!string.Equals(rule.MetricId, o.MetricId, StringComparison.Ordinal)) continue;

            var verdict = MetricEvaluator.Evaluate(rule, _history, o.AccountId, now);
            if (!verdict.Breached) continue;

            // AT MOST ONE TRIGGER PER ACCOUNT PER CALL. Two rules may legitimately share a
            // MetricId — the three rule kinds exist precisely so one number can be judged several
            // ways — so both breaching at once is the designed case, not an edge. Two triggers for
            // one account in one batch reach AlertRouter, which groups per kind, and WebhookPayload
            // then renders "2 accounts — battle.points" with the same alt listed twice at two
            // different values, in the clan channel, the one destination that uses REAL names. The
            // fix belongs here and not in the router: the router's per-batch behaviour is shared
            // with four already-shipped kinds.
            //
            // TIE-BREAK: the first breaching rule in the configured order wins, and the sweep
            // stops. It is deterministic — SetRules copies the caller's list and nothing reorders
            // it, so the same observation against the same rule set always names the same rule —
            // and it is the only tie-break the user actually controls, since the order is theirs.
            // Ranking by severity or by rule kind would need a comparator core has no basis for:
            // a rate, a level and a changed value are not on one scale.
            //
            // GameName carries the metric id rather than a field of its own — four other kinds
            // share that record and none of them wants one. The VALUE gets its own field
            // (MetricValue, trailing and optional, so those four are untouched) because
            // PrivateBytes is a long? and truncating a metric through it renders "at 0" for a 0.79
            // ratio, reintroducing at the display layer the unknown-is-not-zero conflation this
            // core defends in three places.
            return [new AlertTrigger(
                AlertKind.MetricBreach,
                o.AccountId,
                displayName,
                realName,
                o.MetricId,
                null,
                now,
                verdict.Observed)];
        }

        return [];
    }
}
