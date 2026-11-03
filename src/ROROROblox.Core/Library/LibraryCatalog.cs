namespace ROROROblox.Core;

/// <summary>
/// The one Library join, used by the Library page and by every account panel: favourites, saved
/// private servers, and Recent (places in the stats rollup that are not saved: "games played from
/// RoRoRo by any account"). Spec 2026-11-01 §4.2.
/// <para>
/// This is a read. <see cref="ListAsync"/> NEVER triggers a network lookup and never enqueues one:
/// icons come from the saved entry's own thumbnail or from the icon cache's state on disk, null on a
/// miss. A caller that puts an entry on screen asks for its icon with
/// <see cref="IGameIconCache.Request"/>, which is the only thing that fetches.
/// </para>
/// </summary>
public sealed class LibraryCatalog
{
    private readonly IFavoriteGameStore _favorites;
    private readonly IPrivateServerStore _servers;
    private readonly ISessionStatsStore _stats;
    private readonly ISessionHistoryStore _history;
    private readonly IGameIconCache _icons;

    public LibraryCatalog(
        IFavoriteGameStore favorites,
        IPrivateServerStore servers,
        ISessionStatsStore stats,
        ISessionHistoryStore history,
        IGameIconCache icons)
    {
        _favorites = favorites ?? throw new ArgumentNullException(nameof(favorites));
        _servers = servers ?? throw new ArgumentNullException(nameof(servers));
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
    }

    /// <summary>
    /// Favourites, then private servers, then Recent; within each kind newest <see cref="LibraryEntry.LastPlayedUtc"/>
    /// first (never-played last, then by name). A place that is a favourite and was also played comes
    /// back once, as a Favourite, carrying the rollup's last-played and total.
    /// </summary>
    public async Task<IReadOnlyList<LibraryEntry>> ListAsync(CancellationToken ct = default)
    {
        var favorites = await _favorites.ListAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var servers = await _servers.ListAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var stats = await _stats.ReadAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var history = await _history.ListAsync().ConfigureAwait(false);

        var result = new List<LibraryEntry>();

        result.AddRange(Order(favorites.Select(f =>
        {
            stats.Games.TryGetValue(f.PlaceId, out var stat);
            return new LibraryEntry(
                LibraryEntryKind.Favourite, f.PlaceId, PrivateServerId: null, f.Name, f.LocalName,
                IconFor(f.PlaceId, f.ThumbnailUrl), f.IsDefault,
                stat?.LastPlayedUtc, stat?.Uptime, new LaunchTarget.Place(f.PlaceId));
        })));

        result.AddRange(Order(servers.Select(s =>
        {
            var rows = history.Where(r => r.PrivateServerId == s.Id).ToList();
            DateTimeOffset? last = s.LastLaunchedAt;
            if (rows.Count > 0)
            {
                var newest = rows.Max(r => r.LaunchedAtUtc);
                if (last is not { } l || newest > l) last = newest;
            }
            TimeSpan? total = rows.Any(r => r.Duration.HasValue)
                ? rows.Aggregate(TimeSpan.Zero, (acc, r) => acc + (r.Duration ?? TimeSpan.Zero))
                : null;
            return new LibraryEntry(
                LibraryEntryKind.PrivateServer, s.PlaceId, s.Id, s.Name, s.LocalName,
                IconFor(s.PlaceId, s.ThumbnailUrl), s.IsDefault,
                last, total, new LaunchTarget.PrivateServer(s.PlaceId, s.Code, s.CodeKind));
        })));

        var saved = favorites.Select(f => f.PlaceId).ToHashSet();
        result.AddRange(Order(stats.Games
            .Where(kv => kv.Key > 0 && !saved.Contains(kv.Key))
            .Select(kv =>
            {
                var cached = _icons.TryGet(kv.Key);
                var name = !string.IsNullOrWhiteSpace(kv.Value.LastKnownName)
                    ? kv.Value.LastKnownName!
                    : cached?.Name ?? kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new LibraryEntry(
                    LibraryEntryKind.Recent, kv.Key, PrivateServerId: null, name, LocalName: null,
                    cached?.IconUrl, IsDefault: false,
                    kv.Value.LastPlayedUtc, kv.Value.Uptime, new LaunchTarget.Place(kv.Key));
            })));

        return result;
    }

    private string? IconFor(long placeId, string? thumbnail)
        => !string.IsNullOrWhiteSpace(thumbnail) ? thumbnail : _icons.TryGet(placeId)?.IconUrl;

    private static IEnumerable<LibraryEntry> Order(IEnumerable<LibraryEntry> entries)
        => entries
            .OrderByDescending(e => e.LastPlayedUtc.HasValue)
            .ThenByDescending(e => e.LastPlayedUtc)
            .ThenBy(e => e.RenderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.PlaceId);
}
