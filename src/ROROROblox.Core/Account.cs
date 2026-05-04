namespace ROROROblox.Core;

/// <summary>
/// Public, sanitized account record — never carries the cookie. The cookie lives only in the
/// DPAPI-encrypted blob managed by <see cref="IAccountStore"/> and is retrieved on demand via
/// <see cref="IAccountStore.RetrieveCookieAsync"/>.
/// </summary>
public sealed record Account(
    Guid Id,
    string DisplayName,
    string AvatarUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLaunchedAt);
