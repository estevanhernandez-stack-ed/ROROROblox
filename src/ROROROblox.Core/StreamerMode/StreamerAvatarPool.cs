namespace ROROROblox.Core.StreamerMode;

public interface IStreamerAvatarPool
{
    string Next(IReadOnlySet<string> inUse);
    string ResourceUri(string avatarId);
    int Count { get; }
}

/// <summary>
/// A rotating pool of fake avatar ids used by streamer mode to stand in for real Roblox avatar
/// thumbnails on screen. This task owns only the id list and the pack-uri mapping; the images
/// referenced by <see cref="ResourceUri"/> land in a later task (App/StreamerMode/Avatars/{id}.png).
/// </summary>
public sealed class StreamerAvatarPool : IStreamerAvatarPool
{
    // Ids MUST match the shipped image filenames in Task 9 (App/StreamerMode/Avatars/{id}.png).
    private static readonly string[] DefaultIds =
    {
        "noodle", "duck", "potato", "cabbage", "muffin", "artichoke",
        "parsnip", "pixel", "bloxwell", "turnip", "waffle", "pickle",
    };

    private readonly IReadOnlyList<string> _ids;
    private int _cursor;

    public StreamerAvatarPool() : this(DefaultIds) { }

    public StreamerAvatarPool(IReadOnlyList<string> ids)
    {
        if (ids is null || ids.Count == 0)
            throw new ArgumentException("Avatar pool must be non-empty.", nameof(ids));
        _ids = ids;
    }

    public int Count => _ids.Count;

    public string Next(IReadOnlySet<string> inUse)
    {
        for (var i = 0; i < _ids.Count; i++)
        {
            var candidate = _ids[(_cursor + i) % _ids.Count];
            if (!inUse.Contains(candidate))
            {
                _cursor = (_cursor + i + 1) % _ids.Count;
                return candidate;
            }
        }
        var fallback = _ids[_cursor];
        _cursor = (_cursor + 1) % _ids.Count;
        return fallback;
    }

    public string ResourceUri(string avatarId)
        => $"pack://application:,,,/ROROROblox.App;component/StreamerMode/Avatars/{avatarId}.png";
}
