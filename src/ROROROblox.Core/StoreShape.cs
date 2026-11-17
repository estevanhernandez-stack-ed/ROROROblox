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
}
