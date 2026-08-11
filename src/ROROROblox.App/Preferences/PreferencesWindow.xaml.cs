using System.Diagnostics;
using System.Globalization;
using System.Windows;
using ROROROblox.App.Discord;
using ROROROblox.App.Modals;
using ROROROblox.App.Startup;
using ROROROblox.App.Theming;
using ROROROblox.App.Transport;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;
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
    private readonly AlertDispatcher _alertDispatcher;
    private readonly DiscordWebhookSender _webhookSender;
    private readonly WebhookProbe _webhookProbe;
    private readonly DiscordConfigCache _discordConfigCache;

    /// <summary>
    /// What the memory section's blank boxes resolve to on this machine (item 4a). Holds the
    /// injected <see cref="ISystemMemoryProbe"/> rather than reaching for one, so a test can hand
    /// it a known RAM figure — or a failing read — and assert what the section says.
    /// </summary>
    private readonly AutomaticMemorySummary _automaticMemory;

    private bool _suppressClickHandlers; // true while we set the initial check states.

    /// <summary>Channel names reported by the probe for each webhook, if it answered.</summary>
    private string? _mineChannelName;

    private string? _clanChannelName;

    // Loaded once at OnLoaded, mutated on each Discord toggle click, saved whole. A compound
    // record (Presence + Join + webhook fields live in one encrypted blob) needs an in-memory
    // canonical copy — re-reading the store fresh on every click risks a lost-update race if
    // the two Discord checkboxes are clicked in quick succession (the UI message pump can
    // interleave a second click into the first click's await).
    //
    // 2026-08-03: the alert controls below join this same snapshot rather than each doing their
    // own load-modify-save, for exactly the reason above — a fresh read per control would REVERSE
    // this decision and reintroduce the interleave it was written to prevent. The one other writer
    // of this record is MainViewModel.SetAlertsMutedAsync (the row context menu), which cannot run
    // while this dialog is open because the dialog is modal. If Preferences ever becomes
    // modeless, that becomes a real lost update and this whole scheme needs a single owner
    // instead — it is the modality, not the code, that makes this safe today.
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
        DiscordConfigStore discordConfigStore,
        AlertDispatcher alertDispatcher,
        DiscordWebhookSender webhookSender,
        WebhookProbe webhookProbe,
        DiscordConfigCache discordConfigCache,
        ISystemMemoryProbe systemMemoryProbe)
    {
        _automaticMemory = new AutomaticMemorySummary(systemMemoryProbe);
        _alertDispatcher = alertDispatcher;
        _webhookSender = webhookSender;
        _webhookProbe = webhookProbe;
        _discordConfigCache = discordConfigCache;
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

        // Same leak shape as the presence subscription above: this window is transient per-open,
        // MainViewModel is a singleton, so an unsubscribed handler would fire through a closed
        // window's Dispatcher for the rest of the process.
        _mainViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    /// <summary>
    /// Keeps the streamer-mode checkbox a VIEW of the provider rather than a snapshot of it.
    /// <para>
    /// Streamer mode is flippable from three places — this checkbox, the tray menu, and a plugin —
    /// and the tray menu is reachable while this window is open, because a modal
    /// <c>ShowDialog</c> disables only the top-level windows that existed when it opened, and the
    /// tray's <c>ContextMenu</c> popup is created after that. Without this subscription, flipping
    /// the mask from the tray leaves this checkbox reporting the opposite of the truth on the one
    /// control that tells a streamer whether their names are hidden.
    /// </para>
    /// <para>
    /// Wave 1 shipped exactly that bug for one review round: the original control was a two-way
    /// binding, and moving it here turned it into a single read in <c>OnLoaded</c>.
    /// </para>
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.StreamerModeOn)) return;

        // MARSHAL FIRST. This notification arrives on a THREADPOOL thread:
        // StreamerIdentityProvider.SetActiveAsync awaits the settings write with
        // ConfigureAwait(false) and raises Changed on that continuation
        // (StreamerIdentityProvider.cs:107-108). Writing IsChecked from there throws cross-thread
        // instantly — and MainViewModel.StreamerModeOn discards the task it started, so the throw
        // is swallowed with no log line AND it aborts the rest of the Changed multicast, starving
        // every subscriber behind this one. That is the same hazard MainViewModel:261 documents
        // for PressureCrossed, and the rule at MainViewModel:288: the binding engine auto-
        // dispatches, a direct control write does not. The two-way binding this control replaced
        // was marshalling for free; hand-rolling it dropped that.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => OnViewModelPropertyChanged(sender, e)));
            return;
        }

        try
        {
            _suppressClickHandlers = true;
            StreamerModeToggle.IsChecked = _mainViewModel.StreamerModeOn;
        }
        catch (Exception)
        {
            // Never let a UI refresh abort the notification chain — see above. Worst case this
            // checkbox is stale until reopened, which is strictly better than silently breaking
            // every other subscriber's update.
        }
        finally
        {
            _suppressClickHandlers = false;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppressClickHandlers = true;
        try
        {
            SettingsNav.SelectedIndex = 0;
            RunOnLoginToggle.IsChecked = SafeIsStartupEnabled();
            LaunchMainToggle.IsChecked = await _settings.GetLaunchMainOnStartupAsync();
            // v1.18 — the mirror of the Squad Launch modal's careful-mode toggle (F-024). Read on
            // every open, exactly as SquadLaunchWindow.OnLoaded reads it, so the two surfaces agree
            // without either one holding a copy of the value.
            CarefulSquadLaunchToggle.IsChecked = await _settings.GetCarefulSquadLaunchAsync();

            AlwaysShowRecycleToggle.IsChecked = await _settings.GetAlwaysShowRecycleAsync();

            // Streamer mode reads through to IStreamerIdentityProvider via the view model — there is
            // no separate persisted flag here, which is why this reads the VM rather than _settings.
            // The SUBSCRIPTION is the load-bearing half: see OnViewModelPropertyChanged.
            StreamerModeToggle.IsChecked = _mainViewModel.StreamerModeOn;
            _mainViewModel.PropertyChanged += OnViewModelPropertyChanged;

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
                // Alerts share this loaded snapshot — and, unlike presence, work with no Discord
                // application id, so they are populated outside the DiscordPresence null-check below.
                PopulateAlertControls();
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

            // v1.18 — the four memory watchdog settings (F-023). Its own try/catch for the same
            // reason the Discord block above has one: this handler is async void with no outer
            // catch, and AppSettings.LoadAsync only maps IOException and JsonException to defaults
            // — an ACL-blocked settings.json throws UnauthorizedAccessException straight through.
            // Unguarded that would blank the theme picker below, not just this section.
            try
            {
                await PopulateMemoryControlsAsync();
            }
            catch (Exception ex)
            {
                ShowMemoryWarning($"Couldn't read your memory settings: {ex.Message}");
            }

            // Populate the theme picker. Built-ins first, then user-supplied JSON files.
            var themes = await _themeStore.ListAsync();
            ThemePicker.ItemsSource = themes;
            var activeId = await _settings.GetActiveThemeIdAsync() ?? "brand";
            var initialTheme = themes.FirstOrDefault(t =>
                string.Equals(t.Id, activeId, StringComparison.OrdinalIgnoreCase))
                ?? themes.FirstOrDefault();
            ThemePicker.SelectedItem = initialTheme;
            // _suppressClickHandlers is still true here, so OnThemeChanged's SelectionChanged
            // fire above is a no-op — set the description explicitly or a user who never touches
            // the picker never sees it for the theme that is already active.
            UpdateThemeDescription(initialTheme);
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
        UpdateThemeDescription(picked);
        try
        {
            await _themeService.SetActiveAsync(picked.Id);
            // Switching TO a user theme whose edge had to be raised is the same question startup
            // asks — put here too, or the only way to see it would be to restart.
            await EdgeRemediationWindow.AskIfPendingAsync(_themeService, this);
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Sets the one-sentence line under the picker from <see cref="ThemeDescriptions.For"/>.
    /// Collapsed (not Hidden) when the theme is a user theme and the lookup returns null, so no
    /// empty gap is left where the line would have been. Called on initial load and on every
    /// selection change — including a freshly-built user theme (<see cref="OnBuildThemeClick"/>),
    /// which returns null just like any other user theme and must clear a stale built-in line.
    /// </summary>
    private void UpdateThemeDescription(Theme? theme)
    {
        var description = theme is null ? null : ThemeDescriptions.For(theme.Id);
        ThemeDescriptionText.Text = description ?? string.Empty;
        ThemeDescriptionText.Visibility = description is null ? Visibility.Collapsed : Visibility.Visible;
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
                var selected = themes.FirstOrDefault(t =>
                    string.Equals(t.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
                ThemePicker.SelectedItem = selected;
                // Suppressed the same way the initial load is — set explicitly or a prior
                // built-in's sentence stays on screen over a brand-new user theme.
                UpdateThemeDescription(selected);
            }
            finally
            {
                _suppressClickHandlers = false;
            }

            // Asked here rather than inside the builder: the builder is closing at the moment it
            // applies the theme, and a dialog parented to a window on its way out is a flicker.
            await EdgeRemediationWindow.AskIfPendingAsync(_themeService, this);
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

    /// <summary>
    /// The Settings-side half of careful mode. Writes the same persisted value
    /// <c>SquadLaunchWindow.OnCarefulModeToggle</c> writes, through the same
    /// <c>IAppSettings</c> accessor pair — there is one value and two views of it, not two flags.
    /// <para>
    /// On a failed write this reverts from the file rather than leaving the checkbox showing what
    /// the user asked for, which is the same shape the modal uses: a control that shows an unsaved
    /// state is how the two surfaces would start disagreeing.
    /// </para>
    /// </summary>
    private async void OnCarefulSquadLaunchToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        try
        {
            await _settings.SetCarefulSquadLaunchAsync(CarefulSquadLaunchToggle.IsChecked == true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                $"Couldn't save preference: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _suppressClickHandlers = true;
            CarefulSquadLaunchToggle.IsChecked = await _settings.GetCarefulSquadLaunchAsync();
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
        _discordConfigCache.Current = updated;
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
        _discordConfigCache.Current = updated;
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

    // ---------- Alerts (plan 2026-08-03, Task 7) ----------

    /// <summary>
    /// Paint the alert controls from the loaded config. Called from <c>OnLoaded</c> inside the
    /// same guarded block as the presence toggles.
    /// </summary>
    private void PopulateAlertControls()
    {
        SelectDestination(DroppedOutDestination, _discordConfig.DroppedOutDestination);
        SelectDestination(MemoryWarningDestination, _discordConfig.MemoryWarningDestination);
        ShowWebhookMasked(MineWebhookInput, MineWebhookReveal, _discordConfig.MineWebhookUrl);
        ShowWebhookMasked(ClanWebhookInput, ClanWebhookReveal, _discordConfig.ClanWebhookUrl);
        RefreshAlertsStatus();

        // Best-effort, fire-and-forget: name the channel each saved webhook posts to, so a clan
        // webhook sitting in the personal slot is visible on open rather than after it matters.
        if (!string.IsNullOrWhiteSpace(_discordConfig.MineWebhookUrl))
        {
            _ = ProbeWebhookAsync(_discordConfig.MineWebhookUrl, isClan: false);
        }

        if (!string.IsNullOrWhiteSpace(_discordConfig.ClanWebhookUrl))
        {
            _ = ProbeWebhookAsync(_discordConfig.ClanWebhookUrl, isClan: true);
        }
    }

    private static void SelectDestination(System.Windows.Controls.ComboBox combo, AlertDestination destination)
    {
        foreach (var item in combo.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (Equals(item.Tag as string, destination.ToString()))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static AlertDestination ReadDestination(System.Windows.Controls.ComboBox combo) =>
        combo.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string tag }
            && Enum.TryParse<AlertDestination>(tag, out var parsed)
                ? parsed
                : AlertDestination.None;

    /// <summary>
    /// The status line is the feature's honesty, so it is recomputed after every change rather
    /// than set once. <see cref="AlertStatusLine"/> owns which sentence belongs to which state —
    /// see its remarks for why that decision does not live here.
    /// </summary>
    /// <summary>
    /// Persist a settings change AND make it live immediately.
    /// <para>
    /// The cache is what <see cref="AlertDispatcher"/> reads on every dispatch, and it used to be
    /// refreshed only when this dialog closed. That meant a user who set a destination and then sat
    /// watching for an alert with Settings still open got nothing — the dispatcher was still reading
    /// the config from app startup. Measured live: webhook saved 00:07:26, a real memory crossing at
    /// 00:08:46 logged "routed nowhere." A setting that does not take effect until you close the
    /// window it lives in is indistinguishable from a broken feature.
    /// </para>
    /// </summary>
    private async Task SaveDiscordConfigAsync(DiscordConfig updated)
    {
        _discordConfig = updated;
        _discordConfigCache.Current = updated;
        await _discordConfigStore.SaveAsync(updated);
    }

    private void RefreshAlertsStatus() =>
        AlertsStatusLine.Text = AlertStatusLine.Compose(
            _discordConfig,
            _alertDispatcher.MineWebhookRejected,
            _alertDispatcher.ClanWebhookRejected,
            _mineChannelName,
            _clanChannelName);

    private async void OnAlertRoutingChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressClickHandlers) return;

        var updated = _discordConfig with
        {
            DroppedOutDestination = ReadDestination(DroppedOutDestination),
            MemoryWarningDestination = ReadDestination(MemoryWarningDestination),
        };
        RefreshAlertsStatus();

        try
        {
            await SaveDiscordConfigAsync(updated);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save alert routing: {ex.Message}",
                "Preferences", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Commit on LostFocus rather than TextChanged: a webhook URL is pasted whole, and validating
    /// every keystroke would flash four different rejections at someone typing one in by hand.
    /// <para>
    /// One handler serves both fields. The personal and clan webhooks differ only in which slot
    /// they write and which rejection they clear — duplicating this logic per field is how the two
    /// drift into validating differently, and the clan one (the shared room) is the worse half to
    /// get wrong.
    /// </para>
    /// </summary>
    /// <summary>
    /// Puts a saved webhook on screen as a mask, read-only (F-076).
    /// <para>
    /// Read-only matters as much as the mask: a user cannot meaningfully edit a credential whose
    /// middle they cannot see, and letting them try invites a half-edited URL that validates as
    /// garbage. Revealing is the deliberate act that unlocks editing.
    /// </para>
    /// </summary>
    /// <summary>Re-entrancy guard for the programmatic <c>IsChecked</c> write below. Set and cleared
    /// around one assignment only — never held across an await, which is how a suppression flag
    /// swallowed a real user click earlier in this cycle.</summary>
    private bool _syncingWebhookReveal;

    private void ShowWebhookMasked(
        System.Windows.Controls.TextBox input,
        System.Windows.Controls.Primitives.ToggleButton reveal,
        string? savedUrl)
    {
        var hasSaved = !string.IsNullOrWhiteSpace(savedUrl);

        input.Text = WebhookUrlMasker.Mask(savedUrl);
        input.IsReadOnly = hasSaved;

        try
        {
            _syncingWebhookReveal = true;
            reveal.IsChecked = false;
        }
        finally
        {
            _syncingWebhookReveal = false;
        }

        reveal.Content = "Show";
        // Nothing saved means nothing to hide: the field is an ordinary empty box you can paste
        // into, and a Show button over an empty field would be a control that does nothing.
        reveal.Visibility = hasSaved ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Show / Hide on a saved webhook. Revealing also unlocks the field, so "read it" and "replace
    /// it" are the same gesture rather than two controls.
    /// <para>
    /// Wired to Checked and Unchecked rather than Click, and that is load-bearing: a ToggleButton
    /// driven through UI Automation's TogglePattern calls OnToggle and never raises Click. On Click
    /// alone, assistive technology could flip this control's visual state while the field stayed
    /// masked — the button would say one thing and the field show another. Checked/Unchecked fire
    /// for the mouse, the keyboard, and automation alike.
    /// </para>
    /// </summary>
    private void OnRevealWebhookToggled(object sender, RoutedEventArgs e)
    {
        // Only the programmatic re-sync in ShowWebhookMasked is filtered. Deliberately NOT guarded
        // by _suppressClickHandlers: that flag is held across the awaits in OnLoaded, and guarding a
        // user-driven toggle with it swallowed a real click once already (streamer mode, wave 1).
        if (_syncingWebhookReveal) return;

        var isClan = ReferenceEquals(sender, ClanWebhookReveal);
        var input = isClan ? ClanWebhookInput : MineWebhookInput;
        var reveal = isClan ? ClanWebhookReveal : MineWebhookReveal;
        var saved = isClan ? _discordConfig.ClanWebhookUrl : _discordConfig.MineWebhookUrl;

        if (reveal.IsChecked == true)
        {
            input.Text = saved ?? "";
            input.IsReadOnly = false;
            reveal.Content = "Hide";
            input.Focus();
            input.SelectAll();   // so a paste replaces rather than appends
        }
        else
        {
            ShowWebhookMasked(input, reveal, saved);
        }
    }

    private async void OnWebhookCommitted(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;

        var isClan = ReferenceEquals(sender, ClanWebhookInput);
        var input = isClan ? ClanWebhookInput : MineWebhookInput;
        var verdictLine = isClan ? ClanWebhookVerdict : MineWebhookVerdict;
        var saved = isClan ? _discordConfig.ClanWebhookUrl : _discordConfig.MineWebhookUrl;

        // A mask is not an edit. Tabbing past a hidden field must leave the saved value alone and
        // say nothing — without this the mask reaches the validator, fails, and reports "that isn't
        // a webhook URL" about a webhook that is saved and working.
        if (WebhookUrlMasker.IsMasked(input.Text)) return;

        var verdict = WebhookUrlValidator.Inspect(input.Text);
        verdictLine.Text = verdict.Message;

        // Anything that isn't a webhook leaves the SAVED value alone. Clobbering a working webhook
        // because someone pasted an invite over it and then tabbed away is a silent downgrade to
        // desktop-only — precisely the failure the status line exists to surface, self-inflicted.
        if (verdict.Kind is not (WebhookUrlKind.Valid or WebhookUrlKind.Empty)) return;

        var url = verdict.Kind == WebhookUrlKind.Valid ? verdict.NormalizedUrl : null;
        if (url == saved) return;

        // A newly pasted webhook is a fresh chance for a destination the user previously killed.
        DiscordConfig updated;
        if (isClan)
        {
            _clanChannelName = null;
            updated = _discordConfig with { ClanWebhookUrl = url };
            _alertDispatcher.ResetClanRejection();
        }
        else
        {
            _mineChannelName = null;
            updated = _discordConfig with { MineWebhookUrl = url };
            _alertDispatcher.ResetMineRejection();
        }

        // Straight back behind the mask once it is saved. Leaving a just-pasted webhook revealed
        // means the credential stays on screen for the rest of the session — the same exposure,
        // just later.
        ShowWebhookMasked(input, isClan ? ClanWebhookReveal : MineWebhookReveal, url);
        RefreshAlertsStatus();

        try
        {
            await SaveDiscordConfigAsync(updated);
            if (url is not null) await ProbeWebhookAsync(url, isClan);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save the webhook: {ex.Message}",
                "Preferences", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Name the channel a webhook posts to. Doubly worth it with two fields: the mistake this
    /// catches is a clan webhook pasted into the personal slot (or the reverse), which is invisible
    /// until something lands in front of the wrong audience.
    /// </summary>
    private async Task ProbeWebhookAsync(string url, bool isClan)
    {
        var identity = await _webhookProbe.DescribeAsync(url);
        if (identity is null) return;

        if (isClan) { _clanChannelName = identity.ChannelName; }
        else { _mineChannelName = identity.ChannelName; }

        (isClan ? ClanWebhookVerdict : MineWebhookVerdict).Text =
            $"Posts to #{identity.ChannelName} in {identity.GuildName}.";
        RefreshAlertsStatus();
    }

    /// <summary>
    /// Sends a real message down the real path. "It says connected but nothing arrives" is the
    /// failure that otherwise surfaces at 3am, to someone who has already stopped watching.
    /// </summary>
    private async void OnSendTestClick(object sender, RoutedEventArgs e)
    {
        // Test every webhook that is configured, not just the personal one. A clan webhook that
        // silently does not work is the worse failure of the two — nobody notices a channel that
        // never gets posts, and the person who set it up is the last to find out.
        var targets = new List<(string Label, string Url)>();
        if (!string.IsNullOrWhiteSpace(_discordConfig.MineWebhookUrl))
        {
            targets.Add(("My channel", _discordConfig.MineWebhookUrl));
        }

        if (!string.IsNullOrWhiteSpace(_discordConfig.ClanWebhookUrl))
        {
            targets.Add(("Clan channel", _discordConfig.ClanWebhookUrl));
        }

        if (targets.Count == 0)
        {
            AlertsStatusLine.Text = "Paste a webhook URL first — there's nowhere to send a test yet.";
            return;
        }

        SendTestButton.IsEnabled = false;
        AlertsStatusLine.Text = "Sending…";
        try
        {
            var results = new List<string>();
            foreach (var (label, url) in targets)
            {
                var result = await _webhookSender.SendAsync(url,
                    new WebhookPayload("RoRoRo test", "If you can read this, alerts work."));

                results.Add(result switch
                {
                    WebhookSendResult.Sent => $"{label}: sent.",
                    WebhookSendResult.WebhookGone => $"{label}: that webhook no longer exists — make a new one and paste it again.",
                    WebhookSendResult.RateLimited => $"{label}: Discord is rate-limiting us. Wait a minute; the webhook itself looks fine.",
                    _ => $"{label}: couldn't reach Discord.",
                });

                if (result == WebhookSendResult.WebhookGone) RefreshAlertsStatus();
            }

            AlertsStatusLine.Text = string.Join("  ", results);
        }
        finally
        {
            SendTestButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// The step before the step. Every "how to make a webhook" guide starts at Server Settings →
    /// Integrations, which quietly assumes you own a server — and plenty of people have only ever
    /// joined one. A server you make for yourself is free, private, and takes three clicks.
    /// </summary>
    private void OnNoServerHelpClick(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this,
            """
            You need a Discord server of your own. It's free, it can be just you, and nobody else can see it.

            Make one:
              1. Click the + button on the left edge of Discord.
              2. Choose "Create My Own", then skip the questions.
              3. Name it anything — "RoRoRo" works.

            Then make the webhook:
              4. Right-click your new server, then Server Settings.
              5. Integrations, then Webhooks, then New Webhook.
              6. Click Copy Webhook URL, and paste it into the box here.

            Alerts will arrive in that server — on your phone too, as long as Discord is installed on it.
            """,
            "Setting up alerts",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    /// <summary>
    /// Streamer mode, moved here from the main window (audit finding F-008).
    /// <para>
    /// Writes through <see cref="MainViewModel.StreamerModeOn"/>, whose setter fire-and-forgets to
    /// <c>IStreamerIdentityProvider.SetActiveAsync</c> — it does NOT wait for the provider, and the
    /// write can fail silently (see the register's follow-up on that inherited behaviour). What
    /// keeps this checkbox honest is not the write but the read: <see cref="OnViewModelPropertyChanged"/>
    /// re-reads on the provider's confirmation, so the checkbox, the tray checkmark and the row
    /// rendering end up as three views of one source of truth rather than three flags that drift.
    /// </para>
    /// </summary>
    private void OnStreamerModeToggle(object sender, RoutedEventArgs e)
    {
        // NO _suppressClickHandlers guard here, deliberately — it would only ever swallow a real
        // user. Click is raised by ToggleButton.OnClick, which programmatic IsChecked assignment
        // never reaches (that raises Checked/Unchecked instead), so the flag can never protect
        // this handler from our own populate. What it CAN do is eat a genuine click: OnLoaded
        // holds the flag across seven awaits — settings reads, a DPAPI decrypt of discord.dat,
        // theme enumeration — while the window is already visible and interactive. Clicking the
        // box in that window flipped IsChecked (WPF toggles before raising Click) and then hit the
        // guard, so the box looked checked and streamer mode never engaged. Reported live by Este
        // on the wave-1 build, and predicted by the cold review as "failure trace B".
        _mainViewModel.StreamerModeOn = StreamerModeToggle.IsChecked == true;
    }

    /// <summary>Reroll every fake name and avatar at once. Same command the main window used to
    /// invoke; the button moved with the toggle it belongs to.</summary>
    private void OnRerollAllClick(object sender, RoutedEventArgs e)
    {
        if (_mainViewModel.RerollAllCommand.CanExecute(null))
        {
            _mainViewModel.RerollAllCommand.Execute(null);
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

    // ---------- Memory watchdog (v1.18, spec §4.1 — F-023) ----------

    // EVERY SETTING NAME IN THE COMMENTS BELOW IS WRITTEN WITH ITS OWNER, and that is not style.
    // SettingsReachabilityTests' secondary edge matches a bare setting name anywhere in an App
    // .cs/.xaml file — comments and doc comments included, since it reads lines, not syntax — and
    // the one thing it excludes is a preceding "." (a member read is a consumer, not a control).
    // So a bare setting name in a sentence reports that setting reachable, and the same name
    // written with its owner does not. Proved on 2026-08-10 while checking this item's own fence
    // still bites: with the projection setting's control renamed and both of its accessor calls
    // swapped out, the fence still went green on two doc comments. Do not tidy the owners off.

    // WHERE THESE BOUNDS COME FROM, and why they are the view layer's rather than the setter's.
    // spec §4.1: "a setter that clamps is indistinguishable from a setter that worked", so the
    // refusal has to happen where there is a screen to say it on. Every number below is read off
    // what the watchdog actually does with the value, not picked for looking round.
    //
    // RESERVE, floor above zero. The headroom axis is gated on `ReserveBytes > 0`
    // (MemoryWatchdog.cs:226), so a zero reserve silently deletes the third trigger F-082 added —
    // and unlike the cap, IAppSettings declares no meaning for zero here. 256 rather than
    // MemoryDefaults' own 1024 floor because that floor governs a DERIVED value; someone on a small
    // machine asking for a tighter reserve is making a choice, not a mistake. Ceiling 65536 (64 GB)
    // because a reserve above installed RAM latches the headroom warning on permanently, which is
    // the cry-wolf failure MemoryWatchdog's three deadbands exist to prevent.
    private const int ReserveFloorMb = 256;
    private const int ReserveCeilingMb = 65536;

    // CAP, and zero is ACCEPTED rather than being the floor. IAppSettings calls it "a distinct,
    // meaningful user choice: it disables the cap trigger entirely", and MemoryWatchdog.cs:268-275
    // carries a re-arm clause written for exactly that value. Any OTHER value floors at 256,
    // under which a client that has only just launched is already over — MemoryDefaults.
    // ExpectedClientMb is a measured 2650 — so the cap would fire on every healthy client on the
    // first tick. Same 64 GB ceiling, same reason.
    private const int CapFloorMb = 256;
    private const int CapCeilingMb = 65536;

    // PROJECTION, floor 1. The trigger is `minutes < MemoryWatchdog.ProjectionWarnMinutes`
    // (MemoryWatchdog.cs:287) over a `minutes` clamped to non-negative, so zero makes the axis
    // structurally unable to fire — the same defect shape as the F-082 cap that could not trigger
    // at any RAM tier from 16 GB up. Ceiling one day: the watchdog will not claim a slope until it
    // has MinimumObservation (10 minutes) of samples, and past a day any nonzero growth rate leaves
    // the warning permanently on.
    private const int ProjectionFloorMinutes = 1;
    private const int ProjectionCeilingMinutes = 1440;

    /// <summary>
    /// Paints the four memory controls from the file, and READS rather than caches. Hand-editing
    /// <c>settings.json</c> has to keep working, and every accessor here goes back to disk
    /// (<c>AppSettings.LoadAsync</c>) on each call — so re-opening this window shows whatever is in
    /// the file, including a value a text editor put there.
    /// </summary>
    private async Task PopulateMemoryControlsAsync()
    {
        // FIRST, and deliberately. This line reads no settings — it reads the machine — so it must
        // survive a settings read that throws. OnLoaded catches out of this method and paints a
        // "couldn't read your memory settings" warning; if the automatic line were painted after
        // the accessors, that path would leave the boxes blank AND unexplained, which is the exact
        // state this item exists to end.
        AutomaticMemoryLine.Text = _automaticMemory.Describe().Text;

        MemoryWatchdogEnabledToggle.IsChecked = await _settings.GetMemoryWatchdogEnabledAsync();
        MemoryReserveMbInput.Text = FormatOptional(await _settings.GetMemoryReserveMbAsync());
        MemoryCapMbInput.Text = FormatOptional(await _settings.GetMemoryCapMbAsync());
        ProjectionWarnMinutesInput.Text =
            (await _settings.GetProjectionWarnMinutesAsync()).ToString(CultureInfo.InvariantCulture);
        ClearMemoryWarning();
    }

    /// <summary>Blank is not empty, it is "never set" — the state App.xaml.cs derives from.</summary>
    private static string FormatOptional(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private void ShowMemoryWarning(string message)
    {
        // U+25B2, a Segoe UI geometric glyph with Emoji_Presentation=No — not emoji, and the same
        // one AccountSummary's idle chip, MemoryChipFormatter and the compat banner already use.
        // The warning survives with colour removed, which is the whole point of carrying it in the
        // text as well as the brush.
        MemorySettingsWarning.Text = "▲ " + message;
        MemorySettingsWarning.Visibility = Visibility.Visible;
    }

    private void ClearMemoryWarning()
    {
        MemorySettingsWarning.Text = string.Empty;
        MemorySettingsWarning.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Say why, then put the saved value back. Refusing rather than clamping is deliberate: a clamp
    /// and a save look identical from the outside, which is F-023's own defect one level down
    /// (spec §4.1). Putting the saved text back is the second half — a box left showing a refused
    /// number reads as though the number took.
    /// </summary>
    private void Refuse(System.Windows.Controls.TextBox input, string savedText, string reason)
    {
        ShowMemoryWarning(reason);
        input.Text = savedText;
    }

    /// <summary>
    /// An optional whole number. Blank returns <see langword="null"/> — "RoRoRo picks it" — which is
    /// what <c>SettingsBlob.MemoryReserveMb</c> and <c>SettingsBlob.MemoryCapMb</c> ship as, and
    /// what the composition root derives from installed RAM. <paramref name="allowZero"/> lets a
    /// setting whose zero is documented (the cap) through the range check that would otherwise
    /// refuse it.
    /// </summary>
    private static bool TryReadOptionalWholeNumber(
        string? text, int floor, int ceiling, bool allowZero, out int? value, out string refusal)
    {
        value = null;
        refusal = string.Empty;

        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return true;
        }

        var blankHint = allowZero
            ? $"Use a whole number from {floor} to {ceiling}, 0 to turn it off, or leave the box empty and RoRoRo will pick one."
            : $"Use a whole number from {floor} to {ceiling}, or leave the box empty and RoRoRo will pick one.";

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            refusal = $"\"{trimmed}\" isn't a whole number, so nothing was saved. {blankHint}";
            return false;
        }

        if (allowZero && parsed == 0)
        {
            value = 0;
            return true;
        }

        if (parsed < floor || parsed > ceiling)
        {
            refusal = $"{parsed} is outside {floor} to {ceiling}, so nothing was saved. {blankHint}";
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>
    /// A required whole number. <c>SettingsBlob.ProjectionWarnMinutes</c> is a plain <c>int</c> with a real
    /// default, so blank is a refusal here rather than a null — there is no "unset" for it to mean.
    /// </summary>
    private static bool TryReadWholeNumber(
        string? text, int floor, int ceiling, int fallback, out int value, out string refusal)
    {
        value = fallback;
        refusal = string.Empty;

        var trimmed = text?.Trim() ?? string.Empty;
        var hint = $"Use a whole number from {floor} to {ceiling}. The default is {fallback}.";

        if (trimmed.Length == 0)
        {
            refusal = $"This one needs a number, so nothing was saved. {hint}";
            return false;
        }

        if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            refusal = $"\"{trimmed}\" isn't a whole number, so nothing was saved. {hint}";
            return false;
        }

        if (parsed < floor || parsed > ceiling)
        {
            refusal = $"{parsed} is outside {floor} to {ceiling}, so nothing was saved. {hint}";
            return false;
        }

        value = parsed;
        return true;
    }

    private async void OnMemoryWatchdogEnabledToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        try
        {
            await _settings.SetMemoryWatchdogEnabledAsync(MemoryWatchdogEnabledToggle.IsChecked == true);
            ClearMemoryWarning();
        }
        catch (Exception ex)
        {
            // Reported on this section's own line rather than in a MessageBox, so a failure to save
            // and a refusal to accept arrive in the same place and the same voice.
            ShowMemoryWarning($"Couldn't save that: {ex.Message}");
            _suppressClickHandlers = true;
            MemoryWatchdogEnabledToggle.IsChecked = await _settings.GetMemoryWatchdogEnabledAsync();
            _suppressClickHandlers = false;
        }
    }

    /// <summary>
    /// Commit on LostFocus, the same choice the webhook fields make and for the same reason: a
    /// figure is typed whole, and validating every keystroke flashes a rejection at anyone who has
    /// so far typed "2" of "2650".
    /// </summary>
    private async void OnMemoryReserveCommitted(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;

        var saved = FormatOptional(await _settings.GetMemoryReserveMbAsync());
        if (!TryReadOptionalWholeNumber(MemoryReserveMbInput.Text, ReserveFloorMb, ReserveCeilingMb,
                allowZero: false, out var reserveMb, out var refusal))
        {
            Refuse(MemoryReserveMbInput, saved, refusal);
            return;
        }

        try
        {
            await _settings.SetMemoryReserveMbAsync(reserveMb);
            MemoryReserveMbInput.Text = FormatOptional(reserveMb);
            ClearMemoryWarning();
        }
        catch (Exception ex)
        {
            Refuse(MemoryReserveMbInput, saved, $"Couldn't save that: {ex.Message}");
        }
    }

    private async void OnMemoryCapCommitted(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;

        var saved = FormatOptional(await _settings.GetMemoryCapMbAsync());
        // allowZero, and it is not a courtesy: 0 disables the cap trigger and is a documented user
        // choice, which is why the setting is int? instead of sentinel-zero in the first place.
        if (!TryReadOptionalWholeNumber(MemoryCapMbInput.Text, CapFloorMb, CapCeilingMb,
                allowZero: true, out var capMb, out var refusal))
        {
            Refuse(MemoryCapMbInput, saved, refusal);
            return;
        }

        try
        {
            await _settings.SetMemoryCapMbAsync(capMb);
            MemoryCapMbInput.Text = FormatOptional(capMb);
            ClearMemoryWarning();
        }
        catch (Exception ex)
        {
            Refuse(MemoryCapMbInput, saved, $"Couldn't save that: {ex.Message}");
        }
    }

    private async void OnProjectionWarnCommitted(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;

        var saved = (await _settings.GetProjectionWarnMinutesAsync()).ToString(CultureInfo.InvariantCulture);
        // The fallback the refusal message quotes is the same constant the automatic line states,
        // and that constant is pinned to Core/AppSettings.cs's real default by a test. A literal
        // 120 here would be a third place saying it and the only one nothing watches.
        if (!TryReadWholeNumber(ProjectionWarnMinutesInput.Text, ProjectionFloorMinutes,
                ProjectionCeilingMinutes, fallback: AutomaticMemorySummary.ProjectionDefaultMinutes,
                out var minutes, out var refusal))
        {
            Refuse(ProjectionWarnMinutesInput, saved, refusal);
            return;
        }

        try
        {
            await _settings.SetProjectionWarnMinutesAsync(minutes);
            ProjectionWarnMinutesInput.Text = minutes.ToString(CultureInfo.InvariantCulture);
            ClearMemoryWarning();
        }
        catch (Exception ex)
        {
            Refuse(ProjectionWarnMinutesInput, saved, $"Couldn't save that: {ex.Message}");
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

    /// <summary>
    /// Nav rail → page. Five StackPanels live in one Grid and exactly one is visible; the rail's
    /// SelectedIndex picks it.
    /// <para>
    /// Deliberately not a TabControl: a TabControl's headers are chrome we would then have to
    /// re-template to get a shape-based selected state, and its content is rebuilt on tab change,
    /// which would re-run the population every switch. These pages are populated once in OnLoaded
    /// and stay live — the Discord status line subscribes for the window's lifetime, so a page
    /// that gets torn down and rebuilt would drop that subscription silently.
    /// </para>
    /// </summary>
    private void OnNavSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (PageStartup is null) return;   // fires once during InitializeComponent, before the pages exist

        // A single-selection ListBox CAN reach SelectedIndex == -1: Ctrl+click on the already-
        // selected item, or Ctrl+Space with the rail focused, both deselect. Without this the loop
        // below collapses all five pages and the user is left staring at an empty window with no
        // error and no obvious way back. The rail always has a page.
        if (SettingsNav.SelectedIndex < 0)
        {
            SettingsNav.SelectedIndex = 0;   // re-enters this handler with a valid index
            return;
        }

        var pages = new[] { PageStartup, PageAccounts, PageAlerts, PageDiscord, PageAppearance };
        for (var i = 0; i < pages.Length; i++)
        {
            pages[i].Visibility = i == SettingsNav.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
