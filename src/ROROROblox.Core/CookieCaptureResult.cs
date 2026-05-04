namespace ROROROblox.Core;

/// <summary>
/// Outcome of <see cref="ICookieCapture.CaptureAsync"/>. Discriminated union — pattern match
/// to handle each case. <see cref="Success"/> carries the captured cookie + the validated user
/// identity; <see cref="Cancelled"/> = user closed the modal; <see cref="Failed"/> carries a
/// user-facing message (login unsuccessful, WebView2 missing, profile fetch failed).
/// </summary>
public abstract record CookieCaptureResult
{
    private CookieCaptureResult() { }

    public sealed record Success(string Cookie, long UserId, string Username) : CookieCaptureResult;
    public sealed record Cancelled : CookieCaptureResult;
    public sealed record Failed(string Message) : CookieCaptureResult;
}
