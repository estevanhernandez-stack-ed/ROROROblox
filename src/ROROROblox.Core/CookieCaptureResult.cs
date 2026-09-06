namespace ROROROblox.Core;

/// <summary>
/// Why a cookie capture failed, as a key the App can format (and Phase C can translate). The
/// two WebView2 kinds exist because the App routes both to the WebView2-not-installed modal —
/// a decision that used to hang off <c>Message.Contains("WebView2")</c>, which is exactly the
/// fragility the Core string boundary removes
/// (docs/superpowers/specs/2026-09-05-core-string-boundary-design.md).
/// </summary>
public enum CookieCaptureFailureKind
{
    /// <summary>The WebView2 runtime is not installed on this machine.</summary>
    WebView2RuntimeMissing,

    /// <summary>WebView2 initialization threw; <c>Detail</c> carries the exception text.</summary>
    WebView2InitFailed,

    /// <summary>Allocating the per-capture user-data directory threw.</summary>
    UserDataDirFailed,

    /// <summary>Opening the capture window threw before any navigation.</summary>
    StartFailed,

    /// <summary>The capture handler threw mid-flow; <c>Detail</c> carries the exception text.</summary>
    CaptureFailed,

    /// <summary>Roblox's API rejected the captured session cookie (login never took).</summary>
    LoginRejected,

    /// <summary>The cookie was accepted but the profile fetch threw; <c>Detail</c> carries the
    /// exception text.</summary>
    ProfileFetchFailed,
}

/// <summary>
/// Outcome of <see cref="ICookieCapture.CaptureAsync"/>. Discriminated union — pattern match
/// to handle each case. <see cref="Success"/> carries the captured cookie + the validated user
/// identity; <see cref="Cancelled"/> = user closed the modal; <see cref="Failed"/> carries a
/// <see cref="CookieCaptureFailureKind"/> plus optional diagnostic detail — the user-facing
/// sentence is the App's to write (match on <c>Kind</c>, never on message text).
/// </summary>
public abstract record CookieCaptureResult
{
    private CookieCaptureResult() { }

    public sealed record Success(string Cookie, long UserId, string Username) : CookieCaptureResult;
    public sealed record Cancelled : CookieCaptureResult;

    /// <summary><paramref name="Detail"/> is diagnostic data (an exception's text), not a
    /// sentence — it lands after the catalog's headline, and may be logged verbatim.</summary>
    public sealed record Failed(CookieCaptureFailureKind Kind, string? Detail = null) : CookieCaptureResult;
}
