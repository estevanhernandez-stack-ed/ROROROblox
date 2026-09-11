using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Metrics;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// The seam between "a plugin reported a number" and "the alert system knows". Owns the
/// <see cref="MetricAlertCoordinator"/>, enforces the opt-in setting, refuses observations from a
/// clock that disagrees with ours, resolves the subject to the names an alert carries, and RAISES
/// the resulting triggers rather than sending them.
///
/// <para>
/// Raising is the whole design, and it is why this class does not know what a webhook is.
/// <c>App</c> wires <see cref="AlertsRaised"/> to <c>AlertDispatcher.DispatchAsync</c>, exactly as
/// it already wires <c>MainViewModel.AlertsRaised</c>. Everything downstream — per-account mute,
/// the per-(account, kind) cooldown, coalescing, fallback-to-Local — therefore applies to a metric
/// breach for free, and cannot be bypassed from here.
/// </para>
/// </summary>
/// <param name="rules">Plan 2 reads a local file; plan 3 reads a signed manifest. Re-read on every
/// report, so an edited rule takes effect without a restart.</param>
/// <param name="isEnabled">The opt-in gate, read PER REPORT. A user who turns the feature off must
/// have it off on the next report, not on the next launch. A func rather than
/// <c>IAppSettings</c> because this path is synchronous and must not block a gRPC thread on a
/// file read.</param>
/// <param name="resolveNames">Account id to (masked display name, real name). Masked comes first
/// because it is the one nearly every destination gets; only the clan channel is allowed the real
/// one. Off the UI thread this must read <c>MainViewModel.AccountsSnapshot</c>, never
/// <c>Accounts</c>.</param>
public sealed class MetricReportSinkAdapter(
    IMetricRuleSource rules,
    Func<bool> isEnabled,
    Func<Guid, (string Display, string Real)> resolveNames,
    TimeProvider time,
    ILogger<MetricReportSinkAdapter> log) : IMetricReportSink
{
    /// <summary>
    /// How far into the host's future a reported instant may sit before it is treated as a skewed
    /// clock rather than ordinary jitter. A hard "no future instants" rule would reject reports
    /// from any machine running a second fast, which is most of them; this keeps the check a skew
    /// detector rather than a clock-synchronisation requirement.
    /// </summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromSeconds(30);

    private readonly MetricAlertCoordinator _coordinator = new(time);

    /// <summary>Fired when a report produced one or more breaches. <c>App</c> subscribes this to
    /// the alert dispatcher.</summary>
    public event EventHandler<IReadOnlyList<AlertTrigger>>? AlertsRaised;

    public void Report(string subjectId, string metricId, double value, long observedAtUnixMs)
    {
        try
        {
            if (!isEnabled()) return;
            if (string.IsNullOrWhiteSpace(metricId)) return;

            var observedAt = DateTimeOffset.FromUnixTimeMilliseconds(observedAtUnixMs);
            var now = time.GetUtcNow();

            if (observedAt - now > FutureTolerance)
            {
                // Named out loud because the failure is otherwise invisible and ASYMMETRIC: a
                // skewed clock puts every sample outside the rate window, so Rate rules go
                // permanently quiet while Level and Event keep working. "Half the feature stopped"
                // is indistinguishable from "the feature never worked" without this line.
                log.LogInformation(
                    "Dropped a metric report for {MetricId} stamped {Skew} in the future — check the reporter's clock; it is sending local time, not UTC.",
                    metricId, observedAt - now);
                return;
            }

            var active = rules.CurrentRules();
            if (active.Count == 0) return;

            // Guid.Empty is the documented global carrier, shared with AlertKind.UptimeMark: an
            // observation that belongs to no account must still reach the user rather than vanish
            // because a plugin guessed an id wrong.
            var accountId = Guid.TryParse(subjectId, out var parsed) ? parsed : Guid.Empty;
            var (display, real) = resolveNames(accountId);

            // Anything past FutureTolerance already returned above without reaching this line, so
            // "clamped, not dropped" never applies to the case the decision above forbids clamping
            // for. What lands here is at most FutureTolerance of ordinary jitter, and
            // MetricHistory.RatePerMinute windows samples against ITS OWN now (this same
            // TimeProvider) with a strict "at or before now" filter — a sample stamped even one
            // tick ahead of that shared now is silently excluded from every rate window it would
            // otherwise belong to, permanently, for a clock that is merely a few seconds fast.
            // Capping the accepted stamp at our own now treats that jitter as the noise the
            // tolerance already decided it was, rather than by-name reintroducing a second,
            // uncoordinated definition of "now" into a class that already has one.
            var recordedAt = observedAt > now ? now : observedAt;

            _coordinator.SetRules(active);
            var triggers = _coordinator.Observe(
                new MetricObservation(accountId, metricId, value, recordedAt), display, real);

            if (triggers.Count == 0) return;
            AlertsRaised?.Invoke(this, triggers);
        }
        catch (Exception ex)
        {
            // Degrade-safe, like every other alert surface: an alert is a passenger. This runs on
            // a gRPC handler thread and must never take the plugin host down.
            log.LogWarning(ex, "A metric report was dropped.");
        }
    }
}
