namespace ROROROblox.Core;

/// <summary>
/// Roblox's <c>userPresenceType</c> enum from
/// <c>presence.roblox.com/v1/presence/users</c>.
/// </summary>
public enum UserPresenceType
{
    /// <summary>Not online anywhere.</summary>
    Offline = 0,

    /// <summary>Logged into roblox.com / mobile but not in a game.</summary>
    OnlineWebsite = 1,

    /// <summary>Currently in a game (public or private).</summary>
    InGame = 2,

    /// <summary>In Roblox Studio.</summary>
    InStudio = 3,

    /// <summary>Privacy filter or unknown enum value — surface as Offline-equivalent.</summary>
    Invisible = 4,
}

/// <summary>
/// Snapshot of one user's current presence. <see cref="PlaceId"/> + <see cref="GameJobId"/>
/// are populated only when <see cref="PresenceType"/> is <see cref="UserPresenceType.InGame"/>
/// AND the user's privacy settings allow visibility to the requesting cookie's owner.
/// </summary>
public sealed record UserPresence(
    long UserId,
    UserPresenceType PresenceType,
    long? PlaceId,
    string? GameJobId,
    string? LastLocation);
