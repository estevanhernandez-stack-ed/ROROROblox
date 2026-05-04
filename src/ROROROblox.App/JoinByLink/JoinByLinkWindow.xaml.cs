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
    private LaunchTarget? _parsedTarget;

    public LaunchTarget? SelectedTarget { get; private set; }

    public JoinByLinkWindow(IRobloxApi api, string accountDisplayName)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));
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

        _parsedTarget = LaunchTarget.FromUrl(input);
        switch (_parsedTarget)
        {
            case LaunchTarget.PrivateServer ps:
                PreviewLabel.Text = "Private server";
                PreviewDetail.Text = $"Place {ps.PlaceId}  ·  code {Truncate(ps.AccessCode, 18)}";
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
                _parsedTarget = null;
                PreviewBorder.Visibility = Visibility.Collapsed;
                LaunchButton.IsEnabled = false;
                StatusText.Text = "Doesn't look like a Roblox link. Try " +
                                  "https://www.roblox.com/games/<id> or a private server share URL " +
                                  "with ?privateServerLinkCode=...";
                break;
        }
    }

    private void OnLaunchClick(object sender, RoutedEventArgs e)
    {
        if (_parsedTarget is null)
        {
            return;
        }
        SelectedTarget = _parsedTarget;
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? string.Empty :
        s.Length <= max ? s : s[..max] + "…";
}
