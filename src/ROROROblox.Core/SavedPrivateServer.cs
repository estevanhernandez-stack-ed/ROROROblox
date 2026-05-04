namespace ROROROblox.Core;

/// <summary>
/// One Roblox private (VIP) server bookmarked by the user. Distinct from
/// <see cref="LaunchTarget.PrivateServer"/> — that's the launch intent;
/// <see cref="SavedPrivateServer"/> is the persisted record (with name, place name, thumbnail,
/// timestamps).
/// </summary>
/// <remarks>
/// <para>
/// Stored cross-account, NOT per-account. Roblox private servers are scoped to a place +
/// link code; any signed-in user with the link can join. Per-account "stick to this server"
/// behavior is a preference layer, not an ownership boundary.
/// </para>
/// <para>
/// Persisted as plaintext JSON in <c>%LOCALAPPDATA%\ROROROblox\private-servers.json</c>.
/// A private server link is a soft credential — anyone with it can join — but it isn't a
/// password and doesn't unlock the account. Same trust class as a bookmark; same precedent
/// Bloxstrap and earlier multi-account tools use.
/// </para>
/// </remarks>
public sealed record SavedPrivateServer(
    Guid Id,
    long PlaceId,
    string AccessCode,
    string Name,
    string PlaceName,
    string ThumbnailUrl,
    DateTimeOffset AddedAt,
    DateTimeOffset? LastLaunchedAt);
