using System.Windows;
using System.Windows.Controls;
using ROROROblox.Core;

namespace ROROROblox.App.JoinByLink;

/// <summary>
/// One-shot paste-a-URL modal. Live-parses input via <see cref="LaunchTarget.FromUrl"/> and
/// shows a tiny preview ("Public game" / "Private server") so the user knows what they pasted
/// before clicking Launch. <see cref="SelectedTarget"/> is set on success; MainViewModel reads
/// it after <see cref="Window.ShowDialog"/> returns and dispatches the launch.
/// </summary>
internal partial class JoinByLinkWindow : Window
{
    private readonly IRobloxApi _api;
    private readonly Func<string, Task<LaunchTarget?>> _resolveShareUrl;
    private LaunchTarget? _parsedTarget;

    public LaunchTarget? SelectedTarget { get; private set; }

    public JoinByLinkWindow(IRobloxApi api, Func<string, Task<LaunchTarget?>> resolveShareUrl, string accountDisplayName)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _resolveShareUrl = resolveShareUrl ?? throw new ArgumentNullException(nameof(resolveShareUrl));
        InitializeComponent();
        AccountSubtitle.Text = $" / {accountDisplayName}";
        Loaded += (_, _) => UrlInput.Focus();
    }

    private void OnUrlChanged(object sender, TextChangedEventArgs e)
    {
        var input = UrlInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(input))
        {
            _parsedTarget = null;
            PreviewBorder.Visibility = Visibility.Collapsed;
            LaunchButton.IsEnabled = false;
            StatusText.Text = string.Empty;
            return;
        }

        // Cheap sync parse for the immediate preview. Share-token URLs (roblox.com/share?code=...)
        // need an API call; we don't fire that on every keystroke. Instead we recognize the shape,
        // show "Private server (resolving on launch)" preview, and resolve at click time.
        _parsedTarget = LaunchTarget.FromUrl(input);
        switch (_parsedTarget)
        {
            case LaunchTarget.PrivateServer ps:
                PreviewLabel.Text = "Private server";
                var kindLabel = ps.Kind == PrivateServerCodeKind.LinkCode ? "share link" : "access code";
                PreviewDetail.Text = $"Place {ps.PlaceId}  ·  {kindLabel} {Truncate(ps.Code, 18)}";
                PreviewBorder.Visibility = Visibility.Visible;
                LaunchButton.IsEnabled = true;
                StatusText.Text = "We'll launch this account into that VIP server.";
                break;

            case LaunchTarget.Place place:
                PreviewLabel.Text = "Public game";
                PreviewDetail.Text = $"Place {place.PlaceId}";
                PreviewBorder.Visibility = Visibility.Visible;
                LaunchButton.IsEnabled = true;
                StatusText.Text = "We'll launch this account into the public version of that game.";
                break;

            default:
                if (LaunchTarget.TryParseShareLink(input, out _, out var linkType))
                {
                    PreviewLabel.Text = $"Roblox share ({linkType})";
                    PreviewDetail.Text = "We'll resolve this with Roblox when you click Launch.";
                    PreviewBorder.Visibility = Visibility.Visible;
                    LaunchButton.IsEnabled = true;
                    StatusText.Text = string.Empty;
                }
                else
                {
                    _parsedTarget = null;
                    PreviewBorder.Visibility = Visibility.Collapsed;
                    LaunchButton.IsEnabled = false;
                    StatusText.Text = "Doesn't look like a Roblox link. Try " +
                                      "https://www.roblox.com/games/<id>, a private server share URL, " +
                                      "or a roblox.com/share?code=... link.";
                }
                break;
        }
    }

    private async void OnLaunchClick(object sender, RoutedEventArgs e)
    {
        if (_parsedTarget is not null)
        {
            SelectedTarget = _parsedTarget;
            DialogResult = true;
            Close();
            return;
        }

        // Fall through: must be a share-token URL we deferred until click. Resolve now.
        var input = UrlInput.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(input))
        {
            return;
        }
        LaunchButton.IsEnabled = false;
        StatusText.Text = "Resolving share link...";
        try
        {
            var resolved = await _resolveShareUrl(input);
            if (resolved is null)
            {
                StatusText.Text = "Couldn't resolve that share link. Make sure it's a valid private server URL.";
                return;
            }
            SelectedTarget = resolved;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't resolve: {ex.Message}";
        }
        finally
        {
            LaunchButton.IsEnabled = true;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty :
        s.Length <= max ? s : s[..max] + "…";
}
