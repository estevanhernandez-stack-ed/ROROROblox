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

    private readonly string _userDataDir;
    private readonly IRobloxApi _api;
    private readonly ILogger<CookieCaptureWindow> _log;
    private readonly TaskCompletionSource<CookieCaptureResult> _tcs = new();
    private bool _captured;

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
        => _ = TryCaptureAsync("NavigationCompleted");

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
                // the .ROBLOSECURITY isn't usable for API calls. Surface as login-failure.
                _log.LogWarning("CookieCapture: cookie rejected by Roblox API on path={Path}.", uri.AbsolutePath);
                CompleteAndClose(new CookieCaptureResult.Failed("Login was unsuccessful."));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "CookieCapture: profile fetch failed.");
                CompleteAndClose(new CookieCaptureResult.Failed($"Profile fetch failed: {ex.Message}"));
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "CookieCapture: handler threw on trigger={Trigger}.", trigger);
            CompleteAndClose(new CookieCaptureResult.Failed($"Cookie capture failed: {ex.Message}"));
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // If the window closed before we completed (user clicked X or hit ESC), report Cancelled.
        _tcs.TrySetResult(new CookieCaptureResult.Cancelled());
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
