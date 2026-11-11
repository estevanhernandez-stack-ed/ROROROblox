namespace ROROROblox.Core;

/// <summary>
/// Roblox answered the auth-ticket exchange with a status that is neither success nor one of the
/// statuses that already have their own meaning (401 is <see cref="CookieExpiredException"/>, a
/// flagged 403 is <see cref="SessionLimitedException"/>). The code is carried as data so the
/// launcher can tell "Roblox is asking us to slow down" (429) from "Roblox is having trouble"
/// (5xx) without reading a message string.
/// </summary>
/// <remarks>
/// 2026-11-10. Derives from <see cref="InvalidOperationException"/> because that is what this path
/// threw before, with the same message text, so nothing that caught the old type stops catching it.
/// </remarks>
public sealed class RobloxApiStatusException : InvalidOperationException
{
    public RobloxApiStatusException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status Roblox returned.</summary>
    public int StatusCode { get; }
}
