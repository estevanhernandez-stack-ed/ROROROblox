using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.Core.Metrics;

/// <summary>
/// Holds metric breaches for a short, FIXED window and raises them one group per (metric id, rule),
/// so one plugin read that breaches for eight accounts is one alert per destination rather than
/// eight. Measured live on 2026-09-15: eight accounts in one read, ~100 ms apart, became 24
/// notifications, because a plugin reports one <c>ReportMetric</c> per account and
/// <see cref="AlertRouter"/> only groups triggers that arrive in the same call.
///
/// <para>
/// <b>Why here and not in the router.</b> The router's per-call grouping is shared with four
/// already-shipped kinds, and the metric sink is the only path through this class — so "only metric
/// breaches are grouped" is true by construction. Each group leaves as ONE <see cref="Flushed"/>
/// call, the sink hands that call to <c>AlertDispatcher.DispatchAsync</c> whole, and the router's
/// existing per-call behaviour then makes it one alert per destination, with mute, the cooldown and
/// desktop fallback applied exactly as for any other alert. The cooldown slot for a metric breach is
/// (account, metric id) — see <see cref="AlertCooldownKey"/> — so an account cooling down for THIS
/// metric drops out of this group while the rest still alert, and a different stat breaching for
/// the same account in the same read is its own group and its own alert. Two groups never share a
/// call; if they did, the router would fold them into one alert with one rule's title.
/// </para>
/// <para>
/// <b>Fixed, not sliding.</b> A group closes <see cref="Window"/> after its first breach. A sliding
/// window would let a steady trickle postpone an alert indefinitely.
/// </para>
/// <para>
/// <b>Threads.</b> <see cref="Add"/> is called on gRPC handler threads, several at once; the flush
/// runs on a thread-pool timer thread. One lock guards the pending map. A group's timer is created
/// under that lock and its callback takes the lock first, so a flush can never run before its group
/// is registered. The event is raised outside the lock, and nothing it throws escapes: an
/// exception leaving a timer callback terminates the process.
/// </para>
/// <para>
/// <b>Exit.</b> <see cref="Dispose"/> drops what is still pending rather than flushing it. It runs
/// from <c>App.OnExit</c> right after the plugin host stops (corrected 2026-09-15: until then only the
/// container's dispose at the very end of exit reached it, so a group could still send mid-teardown),
/// before the rest of exit tears down the tray and the HTTP clients a flush would reach; the user is
/// at the PC, quitting; and a condition that still holds breaches again on the plugin's next read in
/// the next session. A group whose window had already closed is already dispatching and is not
/// recalled.
/// </para>
/// </summary>
/// <param name="log">The owning sink's logger. Not resolved from DI.</param>
public sealed class MetricBreachBatcher(TimeProvider time, ILogger log) : IDisposable
{
    /// <summary>
    /// How long a group stays open after its first breach. The live read spread was ~100 ms, so
    /// five seconds is fifty times that, and it is small beside the five-minute cooldown and a Rate
    /// window measured in minutes. <c>ScenarioTableTests</c> keeps it within half the smoke
    /// harness's alert window.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time = time ?? throw new ArgumentNullException(nameof(time));
    private readonly ILogger _log = log ?? throw new ArgumentNullException(nameof(log));
    private readonly object _gate = new();
    private readonly Dictionary<GroupKey, PendingGroup> _pending = [];
    private bool _disposed;

    /// <summary>One call per closed group. The sender is this batcher.</summary>
    public event EventHandler<IReadOnlyList<AlertTrigger>>? Flushed;

    public void Add(IReadOnlyList<AlertTrigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);

        lock (_gate)
        {
            if (_disposed) return;

            foreach (var trigger in triggers)
            {
                // KIND is part of the key. Within one five-second window some accounts can breach
                // while others recover on the same metric and rule, and a group is raised as one
                // alert of one kind — so without this the two would be merged and rendered as
                // whichever kind the router picked, with the other half of the accounts listed
                // under a sentence that is false for them.
                var key = new GroupKey(trigger.Kind, trigger.GameName, trigger.Rule);
                if (!_pending.TryGetValue(key, out var group))
                {
                    group = new PendingGroup();
                    _pending[key] = group;
                    group.Timer = _time.CreateTimer(_ => Flush(key), null, Window, Timeout.InfiniteTimeSpan);
                }

                // The same account twice in one window keeps its place and its latest reading.
                var existing = group.Triggers.FindIndex(t => t.AccountId == trigger.AccountId);
                if (existing >= 0) group.Triggers[existing] = trigger;
                else group.Triggers.Add(trigger);
            }
        }
    }

    public void Dispose()
    {
        var dropped = 0;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var group in _pending.Values)
            {
                group.Timer?.Dispose();
                dropped += group.Triggers.Count;
            }
            _pending.Clear();
        }

        if (dropped > 0)
        {
            _log.LogInformation(
                "Exiting with {Count} metric breach(es) still inside the {Seconds}-second grouping window; they were not sent.",
                dropped, Window.TotalSeconds);
        }
    }

    private void Flush(GroupKey key)
    {
        List<AlertTrigger> triggers;
        lock (_gate)
        {
            if (!_pending.Remove(key, out var group)) return;
            group.Timer?.Dispose();
            triggers = group.Triggers;
        }

        try
        {
            Flushed?.Invoke(this, triggers);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "A grouped metric alert for {MetricId} was dropped.", key.MetricId);
        }
    }

    private readonly record struct GroupKey(AlertKind Kind, string? MetricId, MetricRule? Rule);

    private sealed class PendingGroup
    {
        public readonly List<AlertTrigger> Triggers = [];
        public ITimer? Timer;
    }
}
