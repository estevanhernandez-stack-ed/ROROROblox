namespace ROROROblox.Core;

/// <summary>
/// Captures a fresh <c>.ROBLOSECURITY</c> cookie by hosting roblox.com/login in an embedded
/// WebView2 modal. Spec §5.5 + §6.1. Implementation lives in App (depends on WebView2.Wpf);
/// MainViewModel (item 9) consumes via this interface so tests can stub.
///
/// v1.1 shipped one shared <c>%LOCALAPPDATA%\ROROROblox\webview2-data\</c> dir, wiped before each
/// capture to prevent the "still logged in as the previous account" trap during multi-add. That
/// wipe raced msedgewebview2.exe children that outlived the window and re-captured Account #1's
/// cookie, so since commit 981068a (2026-05-09, v1.3.4) each capture gets a fresh GUID-named
/// subdirectory under <c>webview2-data\</c> and stale sibling dirs are swept; see
/// <see cref="WebView2UserDataDirectory"/>. Persistent per-account profiles keyed by Roblox userId
/// are still a later conversation.
/// </summary>
public interface ICookieCapture
{
    Task<CookieCaptureResult> CaptureAsync();
}
