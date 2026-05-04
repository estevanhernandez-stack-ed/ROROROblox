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

    /// <summary>
    /// GET <c>friends.roblox.com/v1/users/{userId}/friends</c> as the cookie's owner. Returns
    /// the friends list with usernames + display names + avatar URLs (bulk-fetched).
    /// Returns empty on network failure or 401-on-this-call (rare, since auth-ticket already
    /// proved the cookie). 401 from this endpoint becomes <see cref="CookieExpiredException"/>.
    /// </summary>
    Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId);

    /// <summary>
    /// POST <c>presence.roblox.com/v1/presence/users</c> with the requested user IDs as the
    /// cookie's owner. Returns presence type + (when the friend is in-game and their privacy
    /// allows it) place id + game job id + last location. Roblox enforces privacy server-side
    /// — friends with restricted presence return <see cref="UserPresenceType.Offline"/> or
    /// <see cref="UserPresenceType.OnlineWebsite"/> regardless of their actual state.
    /// 401 becomes <see cref="CookieExpiredException"/>; other failures return empty.
    /// </summary>
    Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds);

    /// <summary>
    /// Resolve a Roblox <c>roblox.com/share?code=X&amp;type=Y</c> share token into the underlying
    /// target. POSTs <c>apis.roblox.com/sharelinks/v1/resolve-link</c> with the same CSRF dance
    /// the auth-ticket endpoint requires. For <c>type=Server</c> returns the place id + linkCode
    /// pair you can feed straight into <see cref="LaunchTarget.PrivateServer"/>. Returns null on
    /// invalid / expired / network-failed share tokens; the caller surfaces a friendly message.
    /// 401 becomes <see cref="CookieExpiredException"/> — the resolution call is authenticated.
    /// </summary>
    Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType);
}

/// <summary>
/// Result of resolving a Roblox share token. <see cref="LinkType"/> echoes the requested kind
/// (<c>Server</c>, <c>Game</c>, <c>Profile</c>, etc.). For <c>Server</c>, <see cref="PlaceId"/>
/// + <see cref="LinkCode"/> are populated and ready for <see cref="LaunchTarget.PrivateServer"/>.
/// For other types they may be 0 / empty — callers should branch on <see cref="LinkType"/>.
/// </summary>
public sealed record ShareLinkResolution(
    string LinkType,
    long PlaceId,
    string LinkCode);
