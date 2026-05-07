using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ROROROblox.App.Startup;
using ROROROblox.App.Theming;
using ROROROblox.Core;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Theming;

namespace ROROROblox.App.Preferences;

/// <summary>
/// Three sections live here, all immediate-persist (no Apply button — Windows Settings style):
///   - Startup: HKCU Run + LaunchMainOnStartup
///   - Theme: brand picker + builder
///   - Discord integration (v1.2): rich-presence toggle + clan-channel webhook config
///
/// The Discord settings were originally a separate window opened from a separate tray entry;
/// folded in 2026-05-06 (CHECKPOINT 1.5) per builder feedback that all settings should live
/// behind one tray "Preferences..." button instead of scattered tray-menu items.
/// </summary>
internal partial class PreferencesWindow : Window
{
    private static readonly Regex WebhookUrlPattern =
        new(@"^https://discord\.com/api/webhooks/\d+/[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private readonly IAppSettings _settings;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IThemeStore _themeStore;
    private readonly ThemeService _themeService;
    private readonly IDiscordConfig _discordConfig;
    private bool _suppressClickHandlers; // true while we set the initial check states.

    public PreferencesWindow(
        IAppSettings settings,
        IStartupRegistration startupRegistration,
        IThemeStore themeStore,
        ThemeService themeService,
        IDiscordConfig discordConfig)
    {
        _settings = settings;
        _startupRegistration = startupRegistration;
        _themeStore = themeStore;
        _themeService = themeService;
        _discordConfig = discordConfig;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressClickHandlers = true;
        try
        {
            RunOnLoginToggle.IsChecked = SafeIsStartupEnabled();
            LaunchMainToggle.IsChecked = await _settings.GetLaunchMainOnStartupAsync();

            // Populate the theme picker. Built-ins first, then user-supplied JSON files.
            var themes = await _themeStore.ListAsync();
            ThemePicker.ItemsSource = themes;
            var activeId = await _settings.GetActiveThemeIdAsync() ?? "brand";
            ThemePicker.SelectedItem = themes.FirstOrDefault(t =>
                string.Equals(t.Id, activeId, StringComparison.OrdinalIgnoreCase))
                ?? themes.FirstOrDefault();

            // Discord integration block — populated from IDiscordConfig snapshot.
            RichPresenceToggle.IsChecked = _discordConfig.RichPresenceEnabled;
            WebhookUrlBox.Text = _discordConfig.WebhookUrl ?? string.Empty;
            OnLaunchToggle.IsChecked = _discordConfig.WebhookEvents.OnLaunch;
            OnPrivateServerJoinToggle.IsChecked = _discordConfig.WebhookEvents.OnPrivateServerJoin;
            OnNAccountsToggle.IsChecked = _discordConfig.WebhookEvents.OnNAccountsActive;
            ApplyDiscordValidationVisuals();
        }
        finally
        {
            _suppressClickHandlers = false;
        }
    }

    private async void OnThemeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        if (ThemePicker.SelectedItem is not Theme picked) return;
        try
        {
            await _themeService.SetActiveAsync(picked.Id);
        }
        catch
        {
            // best-effort
        }
    }

    private async void OnBuildThemeClick(object sender, RoutedEventArgs e)
    {
        var builder = new ThemeBuilderWindow(_themeStore, _themeService) { Owner = this };
        if (builder.ShowDialog() == true && builder.SavedTheme is { } saved)
        {
            // Refresh the picker so the brand-new theme shows up + is selected.
            _suppressClickHandlers = true;
            try
            {
                var themes = await _themeStore.ListAsync();
                ThemePicker.ItemsSource = themes;
                ThemePicker.SelectedItem = themes.FirstOrDefault(t =>
                    string.Equals(t.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _suppressClickHandlers = false;
            }
        }
    }

    private void OnOpenThemesFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_themeStore.UserThemesFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = _themeStore.UserThemesFolder,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch
        {
            // best-effort
        }
    }

    private void OnRunOnLoginToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        try
        {
            if (RunOnLoginToggle.IsChecked == true)
            {
                _startupRegistration.Enable();
            }
            else
            {
                _startupRegistration.Disable();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't update Windows startup entry: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            // Revert visual state.
            _suppressClickHandlers = true;
            RunOnLoginToggle.IsChecked = SafeIsStartupEnabled();
            _suppressClickHandlers = false;
        }
    }

    private async void OnLaunchMainToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        try
        {
            await _settings.SetLaunchMainOnStartupAsync(LaunchMainToggle.IsChecked == true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't save preference: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _suppressClickHandlers = true;
            LaunchMainToggle.IsChecked = await _settings.GetLaunchMainOnStartupAsync();
            _suppressClickHandlers = false;
        }
    }

    // ---- Discord section ----

    private async void OnRichPresenceToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        await PersistDiscordAsync().ConfigureAwait(true);
    }

    private async void OnDiscordEventToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        await PersistDiscordAsync().ConfigureAwait(true);
    }

    private void OnWebhookUrlChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        ApplyDiscordValidationVisuals();
    }

    private async void OnWebhookUrlLostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        await PersistDiscordAsync().ConfigureAwait(true);
    }

    private void ApplyDiscordValidationVisuals()
    {
        var url = WebhookUrlBox.Text;
        var isValid = !string.IsNullOrWhiteSpace(url) && WebhookUrlPattern.IsMatch(url);
        var isEmpty = string.IsNullOrWhiteSpace(url);

        if (isEmpty)
        {
            WebhookUrlBox.BorderBrush = (Brush)FindResource("DividerBrush");
            WebhookUrlHint.Text = "Paste a webhook URL from your clan's Discord channel admin.";
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

        OnLaunchToggle.IsEnabled = isValid;
        OnPrivateServerJoinToggle.IsEnabled = isValid;
        OnNAccountsToggle.IsEnabled = isValid;
    }

    private async Task PersistDiscordAsync()
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
            await _discordConfig.SaveAsync(snapshot, CancellationToken.None);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't save Discord settings: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private bool SafeIsStartupEnabled()
    {
        try { return _startupRegistration.IsEnabled(); }
        catch { return false; }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
