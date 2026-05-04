namespace ROROROblox.Core;

/// <summary>
/// Metadata for a Roblox game, fetched via <see cref="IRobloxApi.GetGameMetadataByPlaceIdAsync"/>.
/// All fields come from public Roblox endpoints; no cookie required.
/// </summary>
public sealed record GameMetadata(
    long PlaceId,
    long UniverseId,
    string Name,
    string IconUrl);
