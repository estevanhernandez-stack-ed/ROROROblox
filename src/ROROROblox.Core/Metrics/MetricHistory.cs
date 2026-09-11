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
public sealed class MetricHistory(int capacity = 64, int maxSeries = 256)
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

    /// <summary>
    /// The most distinct (account, metric) pairs this history will hold. Metric ids arrive from
    /// a plugin, so a reporter whose id varies would otherwise grow this dictionary without limit
    /// for the lifetime of a process that runs for days.
    /// <para>
    /// At the bound a NEW series is refused and the existing ones are untouched — never
    /// evict-oldest. Eviction would discard a series a rule is actively watching in order to make
    /// room for junk, which is a silent outage of the metric the user configured. Refusing the
    /// newcomer costs only the junk.
    /// </para>
    /// </summary>
    private readonly int _maxSeries = maxSeries > 1
        ? maxSeries
        : throw new ArgumentOutOfRangeException(nameof(maxSeries), "need room for at least two series");

    private void AddSampleToSeries(Series series, Sample sample)
    {
        lock (series.Samples)
        {
            series.Samples.Add(sample);
            if (series.Samples.Count > _capacity)
            {
                series.Samples.RemoveRange(0, series.Samples.Count - _capacity);
                series.HasEvicted = true;
            }
        }
    }

    public void Add(MetricObservation o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var key = (o.AccountId, o.MetricId);
        var sample = new Sample(o.Value, o.ObservedAtUtc);

        // Check if this series already exists. If it does, we accept the sample regardless of the bound.
        if (_series.TryGetValue(key, out var series))
        {
            AddSampleToSeries(series, sample);
            return;
        }

        // New series: check if we're at the bound. This check is advisory under concurrency; the
        // worst case is an overshoot by the width of a burst of distinct new keys arriving
        // concurrently. That is acceptable because the bound targets growth over days, and gRPC
        // handler concurrency bounds the burst width. An exact bound would require a post-add
        // remove that can race with a legitimate concurrent add of the same key — not worth it.
        // The cost of the check itself falls only on first touch of a new key, never on repeat
        // writes to an existing series.
        if (_series.Count >= _maxSeries)
        {
            return;  // Refuse the newcomer; don't evict watched series.
        }

        series = _series.GetOrAdd(key, _ => new Series());
        AddSampleToSeries(series, sample);
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
