using System.Reflection;

namespace ROROROblox.Core.StreamerMode;

public interface IStreamerNamePool
{
    /// <summary>A name not in <paramref name="inUse"/> when one exists; otherwise any name. Never throws, never empty.</summary>
    string Next(IReadOnlySet<string> inUse);
    int Count { get; }
}

/// <summary>
/// A rotating pool of silly, clearly-fake display names used by streamer mode to stand in for
/// real Roblox usernames on screen. The embedded list ships with the app (<c>streamer-names.txt</c>);
/// tests use the <see cref="StreamerNamePool(IReadOnlyList{string})"/> ctor to inject a small fixed list.
/// </summary>
public sealed class StreamerNamePool : IStreamerNamePool
{
    private readonly IReadOnlyList<string> _names;
    private int _cursor;

    public StreamerNamePool() : this(LoadEmbedded()) { }

    public StreamerNamePool(IReadOnlyList<string> names)
    {
        if (names is null || names.Count == 0)
            throw new ArgumentException("Name pool must be non-empty.", nameof(names));
        _names = names;
    }

    public int Count => _names.Count;

    public string Next(IReadOnlySet<string> inUse)
    {
        // One pass from a rotating cursor picks the first free name; falls back to the cursor's
        // name when every name is taken (repeats allowed, never a real name, never a throw).
        for (var i = 0; i < _names.Count; i++)
        {
            var candidate = _names[(_cursor + i) % _names.Count];
            if (!inUse.Contains(candidate))
            {
                _cursor = (_cursor + i + 1) % _names.Count;
                return candidate;
            }
        }
        var fallback = _names[_cursor];
        _cursor = (_cursor + 1) % _names.Count;
        return fallback;
    }

    private static IReadOnlyList<string> LoadEmbedded()
    {
        var asm = typeof(StreamerNamePool).Assembly;
        var resource = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("streamer-names.txt", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        var lines = reader.ReadToEnd()
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        return lines;
    }
}
