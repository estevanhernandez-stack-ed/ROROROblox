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
    DateTimeOffset? LastLaunchedAt,
    bool IsMain = false,
    int SortOrder = 0,
    bool IsSelected = true,
    string? CaptionColorHex = null,
    int? FpsCap = null);
