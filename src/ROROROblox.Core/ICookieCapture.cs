namespace ROROROblox.Core;

/// <summary>
/// Captures a fresh <c>.ROBLOSECURITY</c> cookie by hosting roblox.com/login in an embedded
/// WebView2 modal. Spec §5.5 + §6.1. Implementation lives in App (depends on WebView2.Wpf);
/// MainViewModel (item 9) consumes via this interface so tests can stub.
///
/// v1.1 wipes <c>%LOCALAPPDATA%\ROROROblox\webview2-data\</c> before each capture to prevent
/// the "still logged in as the previous account" trap during multi-add. v1.2 will switch to
/// per-account WebView2 profiles keyed by Roblox userId.
/// </summary>
public interface ICookieCapture
{
    Task<CookieCaptureResult> CaptureAsync();
}
