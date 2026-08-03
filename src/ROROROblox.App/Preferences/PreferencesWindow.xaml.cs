using System.Diagnostics;
using System.Windows;
using ROROROblox.App.Discord;
using ROROROblox.App.Startup;
using ROROROblox.App.Theming;
using ROROROblox.App.Transport;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Theming;
using ROROROblox.Core.Transport;

namespace ROROROblox.App.Preferences;

/// <summary>
/// Two persistent toggles: "Start ROROROblox when Windows starts" (HKCU Run via
/// <see cref="IStartupRegistration"/>) and "Launch my main account when ROROROblox starts"
/// (<see cref="IAppSettings.SetLaunchMainOnStartupAsync"/>). Read both at open, write each
/// independently on click. No Apply button — toggles persist immediately, like Windows
/// Settings.
/// </summary>
internal partial class PreferencesWindow : Window
{
    private readonly IAppSettings _settings;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IThemeStore _themeStore;
    private readonly ThemeService _themeService;
    private readonly IAccountStore _accountStore;
    private readonly IAccountTransport _transport;
    private readonly MainViewModel _mainViewModel;
    private readonly DiscordConfigStore _discordConfigStore;
    private bool _suppressClickHandlers; // true while we set the initial check states.

    // Loaded once at OnLoaded, mutated on each Discord toggle click, saved whole. A compound
    // record (Presence + Join + webhook fields live in one encrypted blob) needs an in-memory
    // canonical copy — re-reading the store fresh on every click risks a lost-update race if
    // the two Discord checkboxes are clicked in quick succession (the UI message pump can
    // interleave a second click into the first click's await).
    private DiscordConfig _discordConfig = new();

    // Set in OnLoaded when DiscordPresence is available, so OnDiscordStatusChanged and OnClosed
    // (subscribe/unsubscribe) both have a reference without re-touching MainViewModel each time.
    private DiscordPresenceService? _discordPresence;

    public PreferencesWindow(
        IAppSettings settings,
        IStartupRegistration startupRegistration,
        IThemeStore themeStore,
        ThemeService themeService,
        IAccountStore accountStore,
        IAccountTransport transport,
        MainViewModel mainViewModel,
        DiscordConfigStore discordConfigStore)
    {
        _settings = settings;
        _startupRegistration = startupRegistration;
        _themeStore = themeStore;
        _themeService = themeService;
        _accountStore = accountStore;
        _transport = transport;
        _mainViewModel = mainViewModel;
        _discordConfigStore = discordConfigStore;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>
    /// Unsubscribes <see cref="OnDiscordStatusChanged"/> from the presence service. Without this,
    /// every Preferences open/close leaks one subscriber onto <see cref="DiscordPresenceService"/>
    /// — a singleton that outlives this (transient, per-open) window — and each leaked subscriber
    /// keeps firing (and marshalling through this closed window's <see cref="Window.Dispatcher"/>)
    /// for the rest of the process.
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        if (_discordPresence is { } presence)
        {
            presence.StatusChanged -= OnDiscordStatusChanged;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressClickHandlers = true;
        try
        {
            RunOnLoginToggle.IsChecked = SafeIsStartupEnabled();
            LaunchMainToggle.IsChecked = await _settings.GetLaunchMainOnStartupAsync();

            AlwaysShowRecycleToggle.IsChecked = await _settings.GetAlwaysShowRecycleAsync();

            // Discord presence + Join (v1.14+ plan). DiscordPresence is only non-null when
            // App.OnStartup found a non-empty Discord:ApplicationId in appsettings.json — see
            // App.WireDiscordPresenceAsync. Null means the feature can never work in this build,
            // so the toggles are disabled rather than left checkable-but-inert.
            //
            // Fix round 1, Finding 3: this block has its own try/catch, separate from the outer
            // one (which has none — an async void Loaded handler with no catch takes the whole
            // app down). DiscordConfigStore.LoadAsync only maps CryptographicException/
            // JsonException to defaults; a locked or ACL-blocked discord.dat throws IOException/
            // UnauthorizedAccessException straight through, and this block runs BEFORE idle +
            // theme population below — an unguarded throw here would blank the rest of the dialog,
            // not just the Discord section.
            try
            {
                _discordConfig = await _discordConfigStore.LoadAsync();
                DiscordPresenceToggle.IsChecked = _discordConfig.PresenceEnabled;
                DiscordJoinToggle.IsChecked = _discordConfig.JoinEnabled;
                // FIX 7 (final whole-branch review, 2026-08-03): Join has no effect while presence
                // is off (DiscordPresenceService.JoinEnabled is now PresenceEnabled && JoinEnabled)
                // — disabling the checkbox here says so instead of leaving it checkable-but-inert.
                DiscordJoinToggle.IsEnabled = _discordConfig.PresenceEnabled;
                if (_mainViewModel.DiscordPresence is { } presence)
                {
                    // Fix round 1, Finding 2: subscribe so the status line stays honest for the
                    // rest of this window's lifetime, not just at this instant — see
                    // OnDiscordStatusChanged's remarks for why a one-time read here isn't enough.
                    _discordPresence = presence;
                    presence.StatusChanged += OnDiscordStatusChanged;
                    DiscordStatusLine.Text = presence.StatusLine;
                }
                else
                {
                    DiscordPresenceToggle.IsEnabled = false;
                    DiscordJoinToggle.IsEnabled = false;
                    DiscordStatusLine.Text = "Discord presence isn't set up for this build.";
                }
            }
            catch (Exception)
            {
                // Same disabled-toggles-with-explanation shape as the "DiscordPresence is null"
                // branch above — a store read failure and "the feature isn't available" look
                // identical to the user, and both are non-fatal to the rest of this dialog.
                DiscordPresenceToggle.IsChecked = false;
                DiscordPresenceToggle.IsEnabled = false;
                DiscordJoinToggle.IsChecked = false;
                DiscordJoinToggle.IsEnabled = false;
                DiscordStatusLine.Text = "Discord presence isn't set up for this build.";
            }

            // v1.8 idle awareness — mute toggle + warn-threshold preset (10/12/15/18 minutes).
            MuteIdleAlertsToggle.IsChecked = await _settings.GetMuteIdleAlertsAsync();
            var thresholdMinutes = await _settings.GetIdleWarnThresholdMinutesAsync();
            IdleWarnThresholdPicker.SelectedItem = IdleWarnThresholdPicker.Items
                .OfType<System.Windows.Controls.ComboBoxItem>()
                .FirstOrDefault(item => string.Equals((string)item.Tag, thresholdMinutes.ToString(), StringComparison.Ordinal))
                ?? IdleWarnThresholdPicker.Items
                .OfType<System.Windows.Controls.ComboBoxItem>()
                .FirstOrDefault(item => string.Equals((string)item.Tag, "15", StringComparison.Ordinal));

            // Populate the theme picker. Built-ins first, then user-supplied JSON files.
            var themes = await _themeStore.ListAsync();
            ThemePicker.ItemsSource = themes;
            var activeId = await _settings.GetActiveThemeIdAsync() ?? "brand";
            ThemePicker.SelectedItem = themes.FirstOrDefault(t =>
                string.Equals(t.Id, activeId, StringComparison.OrdinalIgnoreCase))
                ?? themes.FirstOrDefault();
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

    private async void OnAlwaysShowRecycleToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        var wanted = AlwaysShowRecycleToggle.IsChecked == true;
        try
        {
            await _settings.SetAlwaysShowRecycleAsync(wanted);
            // Push it into the live view model too — the rows bind to the flag, not the file, so
            // without this the change wouldn't show until the next start.
            _mainViewModel.AlwaysShowRecycle = wanted;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't save preference: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _suppressClickHandlers = true;
            AlwaysShowRecycleToggle.IsChecked = await _settings.GetAlwaysShowRecycleAsync();
            _suppressClickHandlers = false;
        }
    }

    /// <summary>
    /// Keeps <see cref="DiscordStatusLine"/> honest for as long as this window is open.
    /// <para>
    /// Fix round 1, Finding 2: <see cref="DiscordPresenceService.StatusLine"/> is a plain property
    /// with no notification of its own — reading it once, right after <c>await ApplyAsync(...)</c>
    /// returns, captures whatever it was set to SYNCHRONOUSLY inside that call (the immediate
    /// "Presence is off." / "Connecting to Discord…" transient), not the real outcome. The real
    /// outcome (Lachee's <c>Ready</c>/<c>ConnectionFailed</c> callbacks) arrives later, off the UI
    /// thread, with nothing else in this window watching for it — so without this subscription the
    /// panel would show a stale value for as long as it stayed open. Subscribed in
    /// <see cref="OnLoaded"/>, unsubscribed in <see cref="OnClosed"/>.
    /// </para>
    /// <para>
    /// <see cref="DiscordPresenceService.StatusChanged"/> can fire from Lachee's background RPC
    /// thread (via <c>Ready</c>/<c>ConnectionFailed</c>) or from this window's own UI thread (via
    /// the toggle handlers below calling <c>ApplyAsync</c> directly) — <c>Dispatcher.Invoke</c>
    /// handles both: it marshals when off-thread and runs synchronously in place when already on
    /// the dispatcher thread, so there's no need to branch on which case this is.
    /// </para>
    /// </summary>
    private void OnDiscordStatusChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_discordPresence is { } presence)
            {
                DiscordStatusLine.Text = presence.StatusLine;
            }
        });
    }

    /// <summary>
    /// "Show what I'm playing on Discord." Mirrors <see cref="OnAlwaysShowRecycleToggle"/>'s shape:
    /// save, then push the change into the live service. The status line is NOT read here after
    /// <c>ApplyAsync</c> returns — see <see cref="OnDiscordStatusChanged"/>'s remarks for why a
    /// one-time read would show a stale value; the subscription set up in <see cref="OnLoaded"/>
    /// is what keeps <see cref="DiscordStatusLine"/> correct through both the immediate transient
    /// and whatever Lachee reports afterward.
    /// </summary>
    private async void OnDiscordPresenceToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        var wanted = DiscordPresenceToggle.IsChecked == true;
        var updated = _discordConfig with { PresenceEnabled = wanted };
        _discordConfig = updated; // update the in-memory copy before the first await — see field doc
        // FIX 7: keep the Join checkbox's enabled state tracking presence live, not just at
        // OnLoaded — Join has no effect while presence is off (DiscordPresenceService.JoinEnabled).
        DiscordJoinToggle.IsEnabled = wanted;
        try
        {
            await _discordConfigStore.SaveAsync(updated);
            if (_mainViewModel.DiscordPresence is { } presence)
            {
                await presence.ApplyAsync(updated);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't save preference: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _suppressClickHandlers = true;
            _discordConfig = await _discordConfigStore.LoadAsync();
            DiscordPresenceToggle.IsChecked = _discordConfig.PresenceEnabled;
            DiscordJoinToggle.IsEnabled = _discordConfig.PresenceEnabled;
            _suppressClickHandlers = false;
        }
    }

    /// <summary>"Let friends join my server from Discord." Same shape as <see cref="OnDiscordPresenceToggle"/>.</summary>
    private async void OnDiscordJoinToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        var wanted = DiscordJoinToggle.IsChecked == true;
        var updated = _discordConfig with { JoinEnabled = wanted };
        _discordConfig = updated;
        try
        {
            await _discordConfigStore.SaveAsync(updated);
            if (_mainViewModel.DiscordPresence is { } presence)
            {
                await presence.ApplyAsync(updated);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't save preference: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _suppressClickHandlers = true;
            _discordConfig = await _discordConfigStore.LoadAsync();
            DiscordJoinToggle.IsChecked = _discordConfig.JoinEnabled;
            _suppressClickHandlers = false;
        }
    }

    private async void OnMuteIdleAlertsToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        try
        {
            await _settings.SetMuteIdleAlertsAsync(MuteIdleAlertsToggle.IsChecked == true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't save preference: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _suppressClickHandlers = true;
            MuteIdleAlertsToggle.IsChecked = await _settings.GetMuteIdleAlertsAsync();
            _suppressClickHandlers = false;
        }
    }

    private async void OnIdleWarnThresholdChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        if (IdleWarnThresholdPicker.SelectedItem is not System.Windows.Controls.ComboBoxItem { Tag: string tag }
            || !int.TryParse(tag, out var minutes))
        {
            return;
        }
        try
        {
            await _settings.SetIdleWarnThresholdMinutesAsync(minutes);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't save preference: {ex.Message}",
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

    // ---------- v1.6.0 — account transport (export / import) entry points ----------

    private void OnExportAccountsClick(object sender, RoutedEventArgs e)
    {
        // Snapshot the live account list from the ViewModel — gives the export checklist each row's
        // RenderName + RobloxUserId (the latter decides exportable vs SkippedNoUserId).
        var accounts = _mainViewModel.Accounts.ToList();
        if (accounts.Count == 0)
        {
            MessageBox.Show(
                this,
                "You don't have any saved accounts to export yet.",
                "Nothing to export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window = new ExportAccountsWindow(_accountStore, _transport, accounts) { Owner = this };
        window.ShowDialog();
    }

    private void OnImportAccountsClick(object sender, RoutedEventArgs e)
    {
        var window = new ImportAccountsWindow(_accountStore, _transport, _mainViewModel) { Owner = this };
        window.ShowDialog();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
