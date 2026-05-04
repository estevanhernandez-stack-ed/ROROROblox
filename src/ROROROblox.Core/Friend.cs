namespace ROROROblox.Core;

/// <summary>
/// One friend of a Roblox account. Returned by
/// <see cref="IRobloxApi.GetFriendsAsync"/>. AvatarUrl may be empty if the bulk thumbnail
/// fetch fails — clients should treat it as best-effort.
/// </summary>
public sealed record Friend(
    long UserId,
    string Username,
    string DisplayName,
    string AvatarUrl);
