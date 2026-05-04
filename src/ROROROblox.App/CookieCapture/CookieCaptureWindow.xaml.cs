using System.Linq;
using System.Threading.Tasks;
using System.Windows;
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
    private readonly TaskCompletionSource<CookieCaptureResult> _tcs = new();
    private bool _captured;

    public CookieCaptureWindow(string userDataDir, IRobloxApi api)
    {
        _userDataDir = userDataDir;
        _api = api;
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

            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.Navigate(LoginUrl);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            CompleteAndClose(new CookieCaptureResult.Failed("WebView2 runtime missing"));
        }
        catch (Exception ex)
        {
            CompleteAndClose(new CookieCaptureResult.Failed($"WebView2 init failed: {ex.Message}"));
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
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

            // Only consider roblox.com pages — avoid responding to embedded analytics, recaptcha, etc.
            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || uri.Host != RobloxHost)
            {
                return;
            }

            // We're "home" once Roblox has redirected to /home, /, or /discover.
            var path = uri.AbsolutePath.TrimEnd('/');
            if (path != string.Empty && path != "/home" && path != "/discover")
            {
                return;
            }

            var cookies = await WebView.CoreWebView2.CookieManager
                .GetCookiesAsync("https://www.roblox.com")
                .ConfigureAwait(true);
            var roblosec = cookies.FirstOrDefault(c => c.Name == RoblosecurityCookieName);
            if (roblosec is null || string.IsNullOrEmpty(roblosec.Value))
            {
                return;
            }

            _captured = true;

            try
            {
                var profile = await _api.GetUserProfileAsync(roblosec.Value).ConfigureAwait(true);
                CompleteAndClose(new CookieCaptureResult.Success(
                    roblosec.Value,
                    profile.UserId,
                    profile.Username));
            }
            catch (CookieExpiredException)
            {
                // The cookie was rejected by Roblox — the WebView2 session looked logged in but
                // the .ROBLOSECURITY isn't usable for API calls. Surface as login-failure.
                CompleteAndClose(new CookieCaptureResult.Failed("Login was unsuccessful."));
            }
            catch (Exception ex)
            {
                CompleteAndClose(new CookieCaptureResult.Failed($"Profile fetch failed: {ex.Message}"));
            }
        }
        catch (Exception ex)
        {
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
