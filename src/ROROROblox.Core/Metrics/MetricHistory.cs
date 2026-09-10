using System.Collections.Concurrent;

namespace ROROROblox.Core.Metrics;

/// <summary>
/// A bounded sample window per (account, metric), and the derivative over it.
///
/// <para>
/// Exists because reported values are usually CUMULATIVE — points so far, total earned — while
/// the interesting question is a RATE. Two samples make a rate; one makes nothing.
/// </para>
/// <para>
/// Thread-safe for concurrent <see cref="Add"/> from a reporting path and reads from an
/// evaluation path, because those are different callers in the shipped shape.
/// </para>
/// </summary>
public sealed class MetricHistory(int capacity = 64)
{
    private readonly record struct Sample(double Value, DateTimeOffset AtUtc);

    private readonly ConcurrentDictionary<(Guid, string), List<Sample>> _series = new();
    private readonly int _capacity = capacity > 1
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), "need room for at least two samples");

    public void Add(MetricObservation o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var list = _series.GetOrAdd((o.AccountId, o.MetricId), _ => []);
        lock (list)
        {
            list.Add(new Sample(o.Value, o.ObservedAtUtc));
            if (list.Count > _capacity) list.RemoveRange(0, list.Count - _capacity);
        }
    }

    public int Count(Guid accountId, string metricId)
    {
        if (!_series.TryGetValue((accountId, metricId), out var list)) return 0;
        lock (list) return list.Count;
    }

    public double? Latest(Guid accountId, string metricId)
    {
        if (!_series.TryGetValue((accountId, metricId), out var list)) return null;
        lock (list) return list.Count == 0 ? null : list[^1].Value;
    }

    /// <summary>
    /// Change per minute across the samples inside <paramref name="window"/>, or <c>null</c> when
    /// that cannot be measured: fewer than two samples in the window, a zero-length span, or a
    /// DECREASE anywhere in it.
    ///
    /// <para>
    /// A decrease means the counter reset — a battle ended, a season rolled — and subtracting
    /// across it reports a large negative rate that reads as catastrophic underperformance.
    /// Returning null makes the caller wait for the window to refill, which is the honest
    /// answer: after a reset there genuinely is no rate yet.
    /// </para>
    /// </summary>
    public double? RatePerMinute(Guid accountId, string metricId, TimeSpan window, DateTimeOffset nowUtc)
    {
        if (!_series.TryGetValue((accountId, metricId), out var list)) return null;

        Sample[] inWindow;
        lock (list)
        {
            var cutoff = nowUtc - window;
            inWindow = [.. list.Where(s => s.AtUtc >= cutoff && s.AtUtc <= nowUtc)];
        }
        if (inWindow.Length < 2) return null;

        for (var i = 1; i < inWindow.Length; i++)
        {
            if (inWindow[i].Value < inWindow[i - 1].Value) return null;   // reset
        }

        var first = inWindow[0];
        var last = inWindow[^1];
        var minutes = (last.AtUtc - first.AtUtc).TotalMinutes;
        return minutes <= 0 ? null : (last.Value - first.Value) / minutes;
    }
}
