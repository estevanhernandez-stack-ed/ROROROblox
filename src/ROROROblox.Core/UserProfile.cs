namespace ROROROblox.Core;

/// <summary>
/// Authenticated-user shape from <c>users.roblox.com/v1/users/authenticated</c>.
/// Username is the unique handle; DisplayName is the user-set label and may equal Username.
/// </summary>
public sealed record UserProfile(long UserId, string Username, string DisplayName);
