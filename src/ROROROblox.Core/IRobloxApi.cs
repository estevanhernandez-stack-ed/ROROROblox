namespace ROROROblox.Core;

/// <summary>
/// Thin wrapper over Roblox's documented public web endpoints. Spec §5.7 + §6.2 steps 1-4.
/// User-Agent is <c>ROROROblox/&lt;version&gt;</c> — never a browser spoof.
/// 401 from any endpoint becomes <see cref="CookieExpiredException"/>.
/// </summary>
public interface IRobloxApi
{
    /// <summary>
    /// Two-call CSRF dance against <c>auth.roblox.com/v1/authentication-ticket</c>:
    /// first POST returns 403 with <c>X-CSRF-TOKEN</c> response header; replay with that header
    /// to receive <c>RBX-Authentication-Ticket</c>. Both POSTs require <c>Content-Type: application/json</c>
    /// on an empty body — Roblox returns 415 without it (caught at spike-time, see spec §5.7).
    /// </summary>
    Task<AuthTicket> GetAuthTicketAsync(string cookie);

    /// <summary>
    /// GET <c>users.roblox.com/v1/users/authenticated</c> for userId / username / displayName.
    /// </summary>
    Task<UserProfile> GetUserProfileAsync(string cookie);

    /// <summary>
    /// GET <c>thumbnails.roblox.com/v1/users/avatar-headshot</c> for the headshot image URL.
    /// </summary>
    Task<string> GetAvatarHeadshotUrlAsync(long userId);

    /// <summary>
    /// Look up game metadata (universe id + display name + icon URL) from a place id.
    /// Uses Roblox's public endpoints; no cookie required. Returns null if any lookup fails
    /// (place not found, network error, malformed response).
    /// </summary>
    Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId);

    /// <summary>
    /// Search Roblox games by free-text query via the public omni-search endpoint. Filters out
    /// non-Game content groups (users, groups). Bulk-fetches icons for all returned games.
    /// Caps results at 20. Returns empty on network failure / malformed response.
    /// </summary>
    Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query);
}
