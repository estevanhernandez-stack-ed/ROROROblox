using Microsoft.Extensions.Logging;

namespace ROROROblox.Core;

/// <summary>
/// Shared idiom for JSON store files that parse but have the wrong shape (for example
/// <c>{"version":1}</c>, which leaves a null list). System.Text.Json accepts those without a
/// <see cref="System.Text.Json.JsonException"/>, so each store checks the parsed blob and sends a wrong
/// shape down the same path it already uses for an unparseable file, instead of letting a null reach a
/// consumer and crash every start.
/// </summary>
internal static class StoreShape
{
    /// <summary>True when the list exists and holds no null element.</summary>
    public static bool ListOk<T>(IEnumerable<T>? items) where T : class
        => items is not null && items.All(i => i is not null);

    /// <summary>True when the map exists and holds no null value.</summary>
    public static bool MapOk<TKey, TValue>(IDictionary<TKey, TValue>? map) where TKey : notnull where TValue : class
        => map is not null && map.Values.All(v => v is not null);

    /// <summary>
    /// The rows that are usable, in order. A null row, or one <paramref name="isUsable"/> refuses, is dropped
    /// and counted: one bad row costs that row, not the file. A missing list is "no rows to keep" and
    /// returns empty with a zero count; the caller decides whether a missing container deserves a warning.
    /// </summary>
    public static List<T> GoodRows<T>(IEnumerable<T?>? items, out int dropped, Func<T, bool>? isUsable = null) where T : class
    {
        var kept = new List<T>();
        dropped = 0;
        if (items is null) return kept;
        foreach (var item in items)
        {
            if (item is null || (isUsable is not null && !isUsable(item))) { dropped++; }
            else { kept.Add(item); }
        }
        return kept;
    }

    /// <summary>The map counterpart of <see cref="GoodRows"/>: entries whose value is null or refused are dropped and counted.</summary>
    public static Dictionary<TKey, TValue> GoodEntries<TKey, TValue>(IDictionary<TKey, TValue?>? map, out int dropped, Func<TValue, bool>? isUsable = null)
        where TKey : notnull where TValue : class
    {
        var kept = new Dictionary<TKey, TValue>();
        dropped = 0;
        if (map is null) return kept;
        foreach (var (key, value) in map)
        {
            if (value is null || (isUsable is not null && !isUsable(value))) { dropped++; }
            else { kept[key] = value; }
        }
        return kept;
    }
}

/// <summary>
/// Logs a wrong-shape file once per store instance, at Warning, with the file name only (never a path
/// under the user's profile, never content): loads run on every read, so a persisting file must not
/// flood the log.
/// </summary>
internal sealed class WrongShapeWarning
{
    private readonly ILogger? _log;
    private int _reported;

    public WrongShapeWarning(ILogger? log) => _log = log;

    public void Report(string path, string treatedAs)
    {
        if (Interlocked.Exchange(ref _reported, 1) == 0)
        {
            _log?.LogWarning("{File} parsed but has the wrong shape; treating it as {TreatedAs}.", Path.GetFileName(path), treatedAs);
        }
    }

    /// <summary>
    /// A file whose container was fine but whose rows were not: the bad rows were dropped, the rest kept.
    /// Same once-per-instance latch as <see cref="Report"/>, because the file keeps those rows until the next
    /// save rewrites it without them and every read in between would say it again.
    /// </summary>
    public void ReportDropped(string path, int dropped)
    {
        if (dropped > 0 && Interlocked.Exchange(ref _reported, 1) == 0)
        {
            _log?.LogWarning("{File} had {Dropped} unusable row(s); they were dropped and the rest kept.", Path.GetFileName(path), dropped);
        }
    }
}
