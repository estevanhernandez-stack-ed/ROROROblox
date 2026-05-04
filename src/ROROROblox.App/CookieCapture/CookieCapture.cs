using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ROROROblox.Core;

namespace ROROROblox.App.CookieCapture;

/// <summary>
/// WebView2-backed implementation of <see cref="ICookieCapture"/>. Spec §5.5 + §6.1.
/// v1.1 wipes <c>%LOCALAPPDATA%\ROROROblox\webview2-data\</c> before every capture to prevent
/// the "still logged in as the previous account" trap during multi-add. v1.2 will switch to
/// per-account profiles.
/// </summary>
public sealed class CookieCapture : ICookieCapture
{
    private static readonly string UserDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        "webview2-data");

    private readonly IRobloxApi _api;

    public CookieCapture(IRobloxApi api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
    }

    public Task<CookieCaptureResult> CaptureAsync()
    {
        // Must run on the UI thread (we're creating a Window).
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("WPF Application has no Dispatcher.");

        if (!dispatcher.CheckAccess())
        {
            return dispatcher.InvokeAsync(CaptureCoreAsync).Task.Unwrap();
        }
        return CaptureCoreAsync();
    }

    private async Task<CookieCaptureResult> CaptureCoreAsync()
    {
        WipeUserDataDir();

        try
        {
            var window = new CookieCaptureWindow(UserDataDir, _api);
            return await window.RunAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            return new CookieCaptureResult.Failed($"Cookie capture failed to start: {ex.Message}");
        }
    }

    private static void WipeUserDataDir()
    {
        if (!Directory.Exists(UserDataDir))
        {
            return;
        }

        try
        {
            Directory.Delete(UserDataDir, recursive: true);
        }
        catch (IOException)
        {
            // Stale handles from a previous WebView2 instance can pin files. Best-effort: leave the
            // directory in place. WebView2 will reuse what's there; the worst case is the user sees
            // their previous session on the login page and has to log out manually.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
