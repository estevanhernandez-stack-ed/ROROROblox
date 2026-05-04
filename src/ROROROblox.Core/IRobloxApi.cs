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
}
