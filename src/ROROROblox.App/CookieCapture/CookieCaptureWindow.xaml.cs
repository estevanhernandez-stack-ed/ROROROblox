using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using ROROROblox.Core;

namespace ROROROblox.App.CookieCapture;

internal partial class CookieCaptureWindow : Window
{
    private const string LoginUrl = "https://www.roblox.com/login";
    private const string RobloxHost = "www.roblox.com";
    private const string RoblosecurityCookieName = ".ROBLOSECURITY";

    /// <summary>
    /// Minimum gap before re-validating a cookie value Roblox already rejected. Bounds the
    /// 401 traffic against the authenticated-user endpoint to at most one probe per debounce
    /// window per navigation burst (plus one delayed probe at debounce expiry when a nav event
    /// was skipped) — repeated rapid hits on that endpoint are the request shape App.xaml.cs
    /// documents as pattern-matching Roblox's anti-fraud heuristics.
    /// </summary>
    private const int RejectedRetryDebounceMs = 3000;

    // Trigger names for the two self-initiated re-checks — used to break scheduling loops:
    // only a REAL navigation event may schedule the debounce-expiry retry.
    private const string RearmRecheckTrigger = "RearmRecheck";
    private const string DebounceRetryTrigger = "DebounceRetry";

    private readonly string _userDataDir;
    private readonly IRobloxApi _api;
    private readonly ILogger<CookieCaptureWindow> _log;
    private readonly TaskCompletionSource<CookieCaptureResult> _tcs = new();
    private bool _captured;
    private bool _firstNavComplete;

    // Rejected-session state (all touched on the dispatcher only — WebView2 WPF events and
    // every await in this class stay on the UI thread). _pendingFailure doubles as the
    // close-time verdict: closing a window that saw a rejection reports Failed, not Cancelled,
    // so the caller can tell "login never took" apart from a deliberate cancel.
    private string? _rejectedCookie;
    private long _rejectedAtTicks;
    private string? _pendingFailure;
    private System.Windows.Threading.DispatcherTimer? _debounceRetryTimer;

    public CookieCaptureWindow(string userDataDir, IRobloxApi api, ILogger<CookieCaptureWindow> log)
    {
        _userDataDir = userDataDir;
        _api = api;
        _log = log;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public Task<CookieCaptureResult> RunAsync()
    {
        Show();
        return _tcs.Task;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _userDataDir);
            await WebView.EnsureCoreWebView2Async(environment);

            // NavigationCompleted catches server-driven page loads; SourceChanged catches the
            // SPA route changes Roblox does post-login. Both fire on roblox.com nav events;
            // the cookie+API check below is the truth signal for "user is authenticated."
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.SourceChanged += OnSourceChanged;
            WebView.CoreWebView2.Navigate(LoginUrl);
            _log.LogInformation("CookieCapture window loaded; navigating to login.");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _log.LogWarning("WebView2 runtime missing.");
            CompleteAndClose(new CookieCaptureResult.Failed("WebView2 runtime missing"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WebView2 init failed.");
            CompleteAndClose(new CookieCaptureResult.Failed($"WebView2 init failed: {ex.Message}"));
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!_firstNavComplete && e.IsSuccess)
        {
            _firstNavComplete = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
        _ = TryCaptureAsync("NavigationCompleted");
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        => _ = TryCaptureAsync("SourceChanged");

    private async Task TryCaptureAsync(string trigger)
    {
        if (_captured)
        {
            return;
        }

        try
        {
            var sourceUrl = WebView.CoreWebView2.Source;
            if (string.IsNullOrEmpty(sourceUrl))
            {
                return;
            }

            // Only consider roblox.com pages — avoid firing on embedded analytics, captcha
            // iframes, telemetry, or third-party hosts inside the page.
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || uri.Host != RobloxHost)
            {
                return;
            }

            var cookies = await WebView.CoreWebView2.CookieManager
                .GetCookiesAsync("https://www.roblox.com")
                .ConfigureAwait(true);

            // Re-check after the await: a navigation burst parks several invocations at the
            // await above, and each resumed continuation is PAST the entry guard. The dispatcher
            // runs continuations one at a time, so whichever resumes first flips _captured in
            // its synchronous section below and this read reliably turns the rest away — without
            // it, every parked invocation launches its own concurrent profile probe.
            if (_captured)
            {
                return;
            }

            var roblosec = cookies.FirstOrDefault(c => c.Name == RoblosecurityCookieName);
            var cookiePresent = roblosec is not null && !string.IsNullOrEmpty(roblosec.Value);

            // Log only path + presence — never the cookie value. This is what we read when a
            // user reports "the window won't close": we can see exactly which path Roblox
            // landed them on and whether the cookie was set yet.
            _log.LogDebug(
                "CookieCapture nav ({Trigger}): path={Path} cookiePresent={CookiePresent}",
                trigger,
                uri.AbsolutePath,
                cookiePresent);

            if (!cookiePresent)
            {
                return;
            }

            // A value Roblox already rejected only re-validates after the debounce — SPA route
            // changes during a 2FA challenge would otherwise fire one 401 probe per step. The
            // debounce (rather than a permanent skip) keeps a recovery path for a same-value
            // cookie that turns valid late (auth propagation), at a bounded request rate.
            if (roblosec!.Value == _rejectedCookie
                && Environment.TickCount64 - _rejectedAtTicks < RejectedRetryDebounceMs)
            {
                // The skipped event may have been the flow's LAST navigation — schedule one
                // probe at debounce expiry so a same-value session that turned valid isn't
                // stranded on a quiescent page. Only real nav events schedule it (the rearm
                // recheck and the timer itself don't), so a permanently-rejected session costs
                // at most one delayed probe per user navigation — never a self-sustaining loop.
                if (trigger is not (RearmRecheckTrigger or DebounceRetryTrigger))
                {
                    ScheduleDebounceRetry();
                }
                return;
            }

            _captured = true;

            try
            {
                var profile = await _api.GetUserProfileAsync(roblosec!.Value).ConfigureAwait(true);
                _log.LogInformation(
                    "CookieCapture success for userId={UserId} on path={Path}.",
                    profile.UserId,
                    uri.AbsolutePath);
                CompleteAndClose(new CookieCaptureResult.Success(
                    roblosec.Value,
                    profile.UserId,
                    profile.Username));
            }
            catch (CookieExpiredException)
            {
                // The cookie was rejected by Roblox — the WebView2 session looked logged in but
                // the .ROBLOSECURITY isn't usable for API calls yet. This is the mid-flow state
                // of a 2FA login (challenge not yet completed), so closing here slams the window
                // shut while the user is still typing the code (followups 2026-06-30 §1).
                // Re-arm and stay open; closing the window later reports _pendingFailure.
                _log.LogWarning(
                    "CookieCapture: cookie rejected by Roblox API on path={Path} — staying open for the login to complete (2FA / partial login).",
                    uri.AbsolutePath);
                RearmAfterRejection(roblosec!.Value, "Roblox didn't accept the login session.");
            }
            catch (Exception ex)
            {
                // Non-401 (429, transient network, endpoint drift). Same trade as the rejected
                // cookie above: closing mid-2FA reproduces the original bug on a different
                // exception type, so re-arm and let the close-time verdict carry the message.
                _log.LogError(ex, "CookieCapture: profile fetch failed — staying open for a later re-check.");
                RearmAfterRejection(roblosec!.Value, $"Profile fetch failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CookieCapture: handler threw on trigger={Trigger}.", trigger);
            CompleteAndClose(new CookieCaptureResult.Failed($"Cookie capture failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Shared re-arm for a validation that didn't take: remember the rejected value (debounce
    /// key), stow the close-time verdict, surface the in-window hint, and re-run the check once
    /// immediately. The immediate re-run closes a race: _captured was true for the whole
    /// in-flight fetch, so a navigation event that landed during it (Roblox's post-login SPA
    /// route change is often the ONLY "login finished" signal) was swallowed by the guard at
    /// the top of TryCaptureAsync. Re-reading now picks up a cookie that changed mid-flight;
    /// an unchanged value stops at the debounce, so this cannot spin.
    /// </summary>
    private void RearmAfterRejection(string rejectedCookieValue, string pendingFailure)
    {
        _rejectedCookie = rejectedCookieValue;
        _rejectedAtTicks = Environment.TickCount64;
        _pendingFailure = pendingFailure;
        RejectionHint.Visibility = Visibility.Visible;
        _captured = false;
        _ = TryCaptureAsync(RearmRecheckTrigger);
    }

    /// <summary>
    /// One-shot re-check just past debounce expiry. Idempotent while pending — a burst of
    /// skipped nav events arms exactly one timer.
    /// </summary>
    private void ScheduleDebounceRetry()
    {
        if (_debounceRetryTimer is not null)
        {
            return;
        }

        var remainingMs = RejectedRetryDebounceMs - (Environment.TickCount64 - _rejectedAtTicks);
        _debounceRetryTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(remainingMs, 100) + 50),
        };
        _debounceRetryTimer.Tick += (_, _) =>
        {
            _debounceRetryTimer?.Stop();
            _debounceRetryTimer = null;
            _ = TryCaptureAsync(DebounceRetryTrigger);
        };
        _debounceRetryTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _debounceRetryTimer?.Stop();
        _debounceRetryTimer = null;

        // Closed before completion (X / ESC). A clean close is a deliberate Cancelled; a close
        // after Roblox rejected the captured session reports that failure instead, so the
        // Add-Account banner and the reauth banner both say WHY nothing was saved rather than
        // pretending the user changed their mind.
        _tcs.TrySetResult(_pendingFailure is null
            ? new CookieCaptureResult.Cancelled()
            : new CookieCaptureResult.Failed(_pendingFailure));
    }

    private void CompleteAndClose(CookieCaptureResult result)
    {
        _tcs.TrySetResult(result);
        if (IsVisible)
        {
            Close();
        }
    }
}
