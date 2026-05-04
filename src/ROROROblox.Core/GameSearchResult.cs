namespace ROROROblox.Core;

/// <summary>
/// One game from <see cref="IRobloxApi.SearchGamesAsync"/>. Carries enough metadata to render
/// in a result list (name, creator, current player count, icon) and add directly to the
/// favorites store (PlaceId + UniverseId).
/// </summary>
public sealed record GameSearchResult(
    long PlaceId,
    long UniverseId,
    string Name,
    string CreatorName,
    long PlayerCount,
    string IconUrl);
