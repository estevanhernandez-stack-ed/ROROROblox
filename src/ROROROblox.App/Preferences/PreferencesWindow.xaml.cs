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
    private readonly AlertDispatcher _alertDispatcher;
    private readonly DiscordWebhookSender _webhookSender;
    private readonly WebhookProbe _webhookProbe;
    private readonly DiscordConfigCache _discordConfigCache;
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
        DiscordConfigCache discordConfigCache)
    {
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

        _suppressClickHandlers = true;
        try
        {
            StreamerModeToggle.IsChecked = _mainViewModel.StreamerModeOn;
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
            RunOnLoginToggle.IsChecked = SafeIsStartupEnabled();
            LaunchMainToggle.IsChecked = await _settings.GetLaunchMainOnStartupAsync();

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
        MineWebhookInput.Text = _discordConfig.MineWebhookUrl ?? "";
        ClanWebhookInput.Text = _discordConfig.ClanWebhookUrl ?? "";
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
    private async void OnWebhookCommitted(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;

        var isClan = ReferenceEquals(sender, ClanWebhookInput);
        var input = isClan ? ClanWebhookInput : MineWebhookInput;
        var verdictLine = isClan ? ClanWebhookVerdict : MineWebhookVerdict;
        var saved = isClan ? _discordConfig.ClanWebhookUrl : _discordConfig.MineWebhookUrl;

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

        input.Text = url ?? "";
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
