using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using ROROROblox.Core;

namespace ROROROblox.App.CookieCapture;

/// <summary>
/// WebView2-backed implementation of <see cref="ICookieCapture"/>. Spec §5.5 + §6.1.
///
/// v1.3.4 (post field-bug fix): each <see cref="CaptureAsync"/> allocates a fresh GUID-named
/// subdirectory under <c>%LOCALAPPDATA%\ROROROblox\webview2-data\</c> via
/// <see cref="WebView2UserDataDirectory"/>, then best-effort sweeps the rest. The previous
/// shared-dir-with-pre-capture-wipe pattern silently failed when stale msedgewebview2.exe
/// children pinned files — second Add Account would re-capture the first account's cookie
/// because WebView2 booted against leftover session state. Per-capture dirs sidestep the
/// race entirely; v1.2's per-account profiles will inherit this shape.
/// </summary>
public sealed class CookieCapture : ICookieCapture
{
    private readonly IRobloxApi _api;
    private readonly WebView2UserDataDirectory _userDataDirectory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Func<string, Task<CookieCaptureResult>> _runCaptureAsync;

    public CookieCapture(IRobloxApi api, WebView2UserDataDirectory userDataDirectory, ILoggerFactory loggerFactory)
        : this(api, userDataDirectory, loggerFactory, runCaptureAsync: null) { }

    /// <summary>
    /// Seam ctor — <paramref name="runCaptureAsync"/> replaces the real CookieCaptureWindow
    /// so the allocate/run/sweep lifecycle is testable without WPF or a WebView2 runtime.
    /// </summary>
    internal CookieCapture(
        IRobloxApi api,
        WebView2UserDataDirectory userDataDirectory,
        ILoggerFactory loggerFactory,
        Func<string, Task<CookieCaptureResult>>? runCaptureAsync)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _userDataDirectory = userDataDirectory ?? throw new ArgumentNullException(nameof(userDataDirectory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _runCaptureAsync = runCaptureAsync ?? RunCaptureWindowAsync;
    }

    private Task<CookieCaptureResult> RunCaptureWindowAsync(string userDataDir)
    {
        var window = new CookieCaptureWindow(userDataDir, _api, _loggerFactory.CreateLogger<CookieCaptureWindow>());
        return window.RunAsync();
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

    // Internal for tests (paired with the seam ctor): CaptureAsync needs a live WPF dispatcher.
    internal async Task<CookieCaptureResult> CaptureCoreAsync()
    {
        string userDataDir;
        try
        {
            userDataDir = _userDataDirectory.AllocateNew();
        }
        catch (Exception ex)
        {
            return new CookieCaptureResult.Failed($"Cookie capture failed to allocate user-data dir: {ex.Message}");
        }

        // Sweep siblings AFTER allocation so we don't accidentally race ourselves out of the
        // dir we're about to hand to WebView2. The exclude param keeps the new dir safe;
        // anything older is best-effort cleanup.
        _userDataDirectory.SweepStale(exclude: userDataDir);

        try
        {
            return await _runCaptureAsync(userDataDir).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            return new CookieCaptureResult.Failed($"Cookie capture failed to start: {ex.Message}");
        }
        finally
        {
            // The dir we just used is a fully logged-in Roblox session (.ROBLOSECURITY in
            // Chromium's cookie store) — a second, unrevoked copy of the crown jewel that
            // account removal never touches. Wipe it the moment the window closes. Best-effort:
            // a still-draining msedgewebview2 child can pin files (logged, never thrown), and
            // the app-start sweep in App.OnStartup catches whatever survives.
            _userDataDirectory.SweepStale(exclude: null);
        }
    }
}
