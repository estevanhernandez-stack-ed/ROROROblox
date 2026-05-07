using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Settings;

/// <summary>
/// Settings UI for v1.2 Discord clan-coordination per spec §5.7. Opens from the tray menu's
/// "Discord integrations..." entry.
///
/// Spec drift: spec §5.7 wanted formal MVVM with a separate ViewModel. The codebase pattern
/// is XAML + immediate-persist code-behind (PreferencesWindow / SettingsWindow). Following
/// the codebase since consistency wins; banner-correct in item 11's docs pass.
///
/// Persistence is immediate (no Apply button) — every toggle/edit calls
/// <see cref="IDiscordConfig.SaveAsync"/>. The store's FileSystemWatcher fires <c>Changed</c>
/// on save, which DiscordRichPresenceService observes to flip its connection state.
/// </summary>
internal partial class DiscordIntegrationsWindow : Window
{
    private static readonly Regex WebhookUrlPattern =
        new(@"^https://discord\.com/api/webhooks/\d+/[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private readonly IDiscordConfig _config;
    private bool _suppressClickHandlers;

    public DiscordIntegrationsWindow(IDiscordConfig config)
    {
        _config = config;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressClickHandlers = true;
        try
        {
            RichPresenceToggle.IsChecked = _config.RichPresenceEnabled;
            WebhookUrlBox.Text = _config.WebhookUrl ?? string.Empty;
            OnLaunchToggle.IsChecked = _config.WebhookEvents.OnLaunch;
            OnPrivateServerJoinToggle.IsChecked = _config.WebhookEvents.OnPrivateServerJoin;
            OnNAccountsToggle.IsChecked = _config.WebhookEvents.OnNAccountsActive;

            ApplyValidationVisuals();
        }
        finally
        {
            _suppressClickHandlers = false;
        }
    }

    private async void OnRichPresenceToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        await PersistAsync().ConfigureAwait(true);
    }

    private async void OnEventToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        await PersistAsync().ConfigureAwait(true);
    }

    private void OnWebhookUrlChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        ApplyValidationVisuals();
    }

    private async void OnWebhookUrlLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        await PersistAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Apply red-border + helper text + per-event toggle disable based on current URL state.
    /// Called on every TextChanged so the user gets immediate feedback as they paste.
    /// </summary>
    private void ApplyValidationVisuals()
    {
        var url = WebhookUrlBox.Text;
        var isValid = !string.IsNullOrWhiteSpace(url) && WebhookUrlPattern.IsMatch(url);
        var isEmpty = string.IsNullOrWhiteSpace(url);

        if (isEmpty)
        {
            WebhookUrlBox.BorderBrush = (Brush)FindResource("DividerBrush");
            WebhookUrlHint.Text = "Get this from your clan's Discord channel admin.";
            WebhookUrlHint.Foreground = (Brush)FindResource("MutedTextBrush");
        }
        else if (isValid)
        {
            WebhookUrlBox.BorderBrush = (Brush)FindResource("CyanBrush");
            WebhookUrlHint.Text = "Looks good. Toggles below are now active.";
            WebhookUrlHint.Foreground = (Brush)FindResource("MutedTextBrush");
        }
        else
        {
            WebhookUrlBox.BorderBrush = (Brush)FindResource("MagentaBrush");
            WebhookUrlHint.Text = "Webhook URL must come from your clan's Discord channel admin.";
            WebhookUrlHint.Foreground = (Brush)FindResource("MagentaBrush");
        }

        // Per-event toggles disabled until the URL is valid — keeps the user from opting in
        // to events that would silently no-op anyway.
        OnLaunchToggle.IsEnabled = isValid;
        OnPrivateServerJoinToggle.IsEnabled = isValid;
        OnNAccountsToggle.IsEnabled = isValid;
    }

    private async Task PersistAsync()
    {
        var url = WebhookUrlBox.Text;
        var trimmed = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        var snapshot = new DiscordConfigSnapshot(
            RichPresenceEnabled: RichPresenceToggle.IsChecked == true,
            WebhookUrl: trimmed,
            WebhookEvents: new DiscordWebhookEvents(
                OnLaunch: OnLaunchToggle.IsChecked == true,
                OnPrivateServerJoin: OnPrivateServerJoinToggle.IsChecked == true,
                OnNAccountsActive: OnNAccountsToggle.IsChecked == true));

        try
        {
            await _config.SaveAsync(snapshot, CancellationToken.None);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't save Discord settings: {ex.Message}",
                "Discord integrations",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
