namespace ROROROblox.Core;

/// <summary>
/// Raised when a Roblox endpoint returns 401 — the cookie is invalid or expired.
/// <see cref="IRobloxLauncher"/> (item 6) catches this and converts to <c>LaunchResult.CookieExpired</c>;
/// the MainViewModel (item 9) flips the row to the yellow "Session expired" badge.
/// </summary>
public sealed class CookieExpiredException : Exception
{
    public CookieExpiredException() : base("Roblox cookie is invalid or expired.") { }
    public CookieExpiredException(string message) : base(message) { }
    public CookieExpiredException(string message, Exception inner) : base(message, inner) { }
}
