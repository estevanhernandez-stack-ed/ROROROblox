namespace ROROROblox.Core;

/// <summary>
/// One Roblox game saved by the user. <see cref="IsDefault"/> marks the one Launch As uses
/// when no explicit place URL is passed. Persisted as JSON in
/// <c>%LOCALAPPDATA%\ROROROblox\favorites.json</c>; not secret, no DPAPI envelope.
/// </summary>
public sealed record FavoriteGame(
    long PlaceId,
    long UniverseId,
    string Name,
    string ThumbnailUrl,
    bool IsDefault,
    DateTimeOffset AddedAt);
