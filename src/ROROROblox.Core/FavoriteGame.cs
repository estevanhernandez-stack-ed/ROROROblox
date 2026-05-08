namespace ROROROblox.Core;

/// <summary>
/// One Roblox game saved by the user. <see cref="IsDefault"/> marks the one Launch As uses
/// when no explicit place URL is passed. Persisted as JSON in
/// <c>%LOCALAPPDATA%\ROROROblox\favorites.json</c>; not secret, no DPAPI envelope.
/// </summary>
/// <remarks>
/// <see cref="LocalName"/> is a per-user nickname override that wins over <see cref="Name"/>
/// at every render surface. Roblox-side name refreshes never touch <see cref="LocalName"/>;
/// re-adding the same <see cref="PlaceId"/> preserves it. v1.3.x.
/// </remarks>
public sealed record FavoriteGame(
    long PlaceId,
    long UniverseId,
    string Name,
    string ThumbnailUrl,
    bool IsDefault,
    DateTimeOffset AddedAt,
    string? LocalName = null);
