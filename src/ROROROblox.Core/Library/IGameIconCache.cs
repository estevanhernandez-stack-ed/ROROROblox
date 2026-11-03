namespace ROROROblox.Core;

/// <summary>What the icon cache knows about one place. Non-secret public metadata.</summary>
public sealed record GameIconInfo(string Name, long UniverseId, string IconUrl, DateTimeOffset FetchedAtUtc);

/// <summary>Payload for <see cref="IGameIconCache.IconResolved"/>.</summary>
public sealed class GameIconResolvedEventArgs : EventArgs
{
    public GameIconResolvedEventArgs(long placeId, GameIconInfo info)
    {
        PlaceId = placeId;
        Info = info;
    }

    public long PlaceId { get; }
    public GameIconInfo Info { get; }
}

/// <summary>
/// Name, universe id and icon for places that are not saved (Recent has no thumbnail of its own).
/// Answers from <c>game-icons.json</c>; a miss is fetched only when a caller asks for that one place
/// with <see cref="Request"/>, and the fetch rides <see cref="AccountFactsService"/>'s single paced
/// worker. Nothing is ever fetched for a place nobody asked about.
/// </summary>
public interface IGameIconCache
{
    /// <summary>Disk/memory state only. Never fetches, never enqueues.</summary>
    GameIconInfo? TryGet(long placeId);

    /// <summary>
    /// Ask for one place that is on screen. A hit does nothing; a miss enqueues exactly one lookup
    /// (a second ask while it is outstanding, or after it failed this run, enqueues nothing).
    /// Returns true when a lookup was enqueued.
    /// </summary>
    bool Request(long placeId);

    /// <summary>Raised on the worker thread after a lookup landed and the file was written. Subscribers marshal.</summary>
    event EventHandler<GameIconResolvedEventArgs>? IconResolved;

    /// <summary>
    /// The worker's entry point: do the lookup now, persist it, raise <see cref="IconResolved"/>.
    /// Returns true when a request went out. Public so the worker (and tests) can drive it; callers
    /// that want an icon use <see cref="Request"/>.
    /// </summary>
    Task<bool> ResolveAsync(long placeId, CancellationToken ct = default);

    /// <summary>
    /// Give the cache the queue that <see cref="Request"/> enqueues onto. Called once by the worker
    /// that will run the lookups. Until then a miss enqueues nothing.
    /// </summary>
    void AttachQueue(Func<long, bool> enqueue);
}
