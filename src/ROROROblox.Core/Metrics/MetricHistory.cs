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

    private sealed class Series
    {
        public readonly List<Sample> Samples = [];
        /// <summary>Set once <see cref="Samples"/> has actually dropped an old sample. Distinct
        /// from "full": a series holding exactly capacity items has evicted nothing, and its
        /// history is complete.</summary>
        public bool HasEvicted;
    }

    private readonly ConcurrentDictionary<(Guid, string), Series> _series = new();
    private readonly int _capacity = capacity > 1
        ? capacity
        : throw new ArgumentOutOfRangeException(nameof(capacity), "need room for at least two samples");

    public void Add(MetricObservation o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var series = _series.GetOrAdd((o.AccountId, o.MetricId), _ => new Series());
        lock (series.Samples)
        {
            series.Samples.Add(new Sample(o.Value, o.ObservedAtUtc));
            if (series.Samples.Count > _capacity)
            {
                series.Samples.RemoveRange(0, series.Samples.Count - _capacity);
                series.HasEvicted = true;
            }
        }
    }

    public int Count(Guid accountId, string metricId)
    {
        if (!_series.TryGetValue((accountId, metricId), out var series)) return 0;
        lock (series.Samples) return series.Samples.Count;
    }

    public double? Latest(Guid accountId, string metricId)
    {
        if (!_series.TryGetValue((accountId, metricId), out var series)) return null;
        lock (series.Samples) return series.Samples.Count == 0 ? null : series.Samples[^1].Value;
    }

    /// <summary>True when the two most recent samples differ. Used by
    /// <see cref="MetricRuleKind.Event"/>; false when there are fewer than two.</summary>
    public bool Changed(Guid accountId, string metricId)
    {
        if (!_series.TryGetValue((accountId, metricId), out var series)) return false;
        lock (series.Samples) return series.Samples.Count >= 2 && series.Samples[^1].Value != series.Samples[^2].Value;
    }

    /// <summary>
    /// Change per minute across the samples inside <paramref name="window"/>, or <c>null</c> when
    /// that cannot be measured: fewer than two samples in the window, a zero-length span, a
    /// DECREASE anywhere in it, or trimmed in-window data due to capacity bounds.
    ///
    /// <para>
    /// A decrease means the counter reset — a battle ended, a season rolled — and subtracting
    /// across it reports a large negative rate that reads as catastrophic underperformance.
    /// Returning null makes the caller wait for the window to refill, which is the honest
    /// answer: after a reset there genuinely is no rate yet.
    /// </para>
    /// <para>
    /// When the series is at capacity and the oldest retained sample is newer than the window
    /// cutoff, prior in-window samples have been trimmed and the rate would be measured over a
    /// truncated span, under-reporting the true rate. Returning null is the honest answer: data loss
    /// means we cannot trust the result.
    /// </para>
    /// </summary>
    public double? RatePerMinute(Guid accountId, string metricId, TimeSpan window, DateTimeOffset nowUtc)
    {
        if (!_series.TryGetValue((accountId, metricId), out var series)) return null;

        Sample[] inWindow;
        bool trimmedInWindowData = false;
        lock (series.Samples)
        {
            var cutoff = nowUtc - window;
            inWindow = [.. series.Samples.Where(s => s.AtUtc >= cutoff && s.AtUtc <= nowUtc)];
            // If eviction has occurred and the oldest retained sample is newer than the cutoff,
            // in-window data may have been trimmed, so we can't trust the rate.
            if (series.HasEvicted && series.Samples[0].AtUtc > cutoff)
            {
                trimmedInWindowData = true;
            }
        }
        if (trimmedInWindowData) return null;
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
