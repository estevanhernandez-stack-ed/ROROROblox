using System.Windows;
using System.Windows.Controls;
using ROROROblox.App.Localization;
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

    public bool SaveToLibrary => SaveCheckBox.IsChecked == true;

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
                PreviewLabel.Text = Loc.Get("Shell_Join_PrivateServer");
                var kindLabel = ps.Kind == PrivateServerCodeKind.LinkCode ? Loc.Get("Shell_Join_ShareLink") : Loc.Get("Shell_Join_AccessCode");
                PreviewDetail.Text = Loc.Format("Shell_Join_PlaceCode", ps.PlaceId, kindLabel, Truncate(ps.Code, 18));
                PreviewBorder.Visibility = Visibility.Visible;
                LaunchButton.IsEnabled = true;
                StatusText.Text = Loc.Get("Shell_Join_VipServerHint");
                break;

            case LaunchTarget.Place place:
                PreviewLabel.Text = Loc.Get("Shell_Join_PublicGame");
                PreviewDetail.Text = Loc.Format("Shell_Join_Place", place.PlaceId);
                PreviewBorder.Visibility = Visibility.Visible;
                LaunchButton.IsEnabled = true;
                StatusText.Text = Loc.Get("Shell_Join_PublicGameHint");
                break;

            default:
                if (LaunchTarget.TryParseShareLink(input, out _, out var linkType))
                {
                    PreviewLabel.Text = Loc.Format("Shell_Join_RobloxShare", linkType);
                    PreviewDetail.Text = Loc.Get("Shell_Join_ResolveHint");
                    PreviewBorder.Visibility = Visibility.Visible;
                    LaunchButton.IsEnabled = true;
                    StatusText.Text = string.Empty;
                }
                else
                {
                    _parsedTarget = null;
                    PreviewBorder.Visibility = Visibility.Collapsed;
                    LaunchButton.IsEnabled = false;
                    StatusText.Text = Loc.Get("Shell_Join_NotALink");
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
        StatusText.Text = Loc.Get("Shell_Join_Resolving");
        try
        {
            var resolved = await _resolveShareUrl(input);
            if (resolved is null)
            {
                StatusText.Text = Loc.Get("Shell_Join_CouldntResolve");
                return;
            }
            SelectedTarget = resolved;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Format("Shell_Join_CouldntResolveEx", ex.Message);
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
