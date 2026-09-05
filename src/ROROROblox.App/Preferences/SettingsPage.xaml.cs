using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
/// The Settings destination, hosted by the shell (F-013 — formerly <c>PreferencesWindow</c>, a
/// modal dialog). Toggles read at load and persist immediately on click, like Windows Settings —
/// no Apply button. Going modeless is safe because every shared record this page edits now has a
/// single owner (<see cref="DiscordConfigService"/> for Discord; <see cref="IAppSettings"/>'
/// internal gate for the rest). Implements <see cref="IDisposable"/> for the subscriptions the
/// window used to release on <c>Closed</c>; the shell disposes pages when it closes.
/// </summary>
internal partial class SettingsPage : UserControl, IDisposable
{
    private readonly IAppSettings _settings;
    private readonly IStartupRegistration _startupRegistration;
    private readonly IThemeStore _themeStore;
    private readonly ThemeService _themeService;
    private readonly IAccountStore _accountStore;
    private readonly IAccountTransport _transport;
    private readonly MainViewModel _mainViewModel;
    private readonly DiscordConfigService _discordConfigService;
    private readonly AlertDispatcher _alertDispatcher;
    private readonly DiscordWebhookSender _webhookSender;
    private readonly WebhookProbe _webhookProbe;
    private readonly ROROROblox.Core.Notify.PhoneNotifyConfigService _phoneNotifyService;
    private readonly ROROROblox.App.Notify.PhoneAlertSender _phoneAlertSender;

    /// <summary>
    /// What the memory section's blank boxes resolve to on this machine (item 4a). Holds the
    /// injected <see cref="ISystemMemoryProbe"/> rather than reaching for one, so a test can hand
    /// it a known RAM figure — or a failing read — and assert what the section says.
    /// </summary>
    private readonly AutomaticMemorySummary _automaticMemory;

    /// <summary>
    /// What the themes folder had to say when this window opened, and what
    /// <see cref="ThemeStatusLine"/> falls back to.
    /// <para>
    /// ONE LINE, TWO REPORTERS, so one of them has to be the resting state. A successful theme
    /// change is silent (<c>spec.md &gt; §5</c>) — but silent means "says nothing about the save",
    /// not "blanks the line", and blanking it would wipe a bad-file report the user has not fixed
    /// yet. A bad file is still a bad file after a theme change that worked.
    /// </para>
    /// </summary>
    private ThemeStatusSummary.Line _themeFolderStatus = ThemeStatusSummary.Silent;

    private bool _suppressClickHandlers; // true while we set the initial check states.

    /// <summary>Channel names reported by the probe for each webhook, if it answered.</summary>
    private string? _mineChannelName;

    private string? _clanChannelName;

    // The single owner replaced the snapshot that used to live here (F-013 prerequisite,
    // 2026-08-21). The old design kept an in-memory copy, mutated it on every click, and saved it
    // whole — safe only because this dialog was modal and the one other writer (the row context
    // menu's mute) could not run underneath it. Its own comment said so: "it is the modality, not
    // the code, that makes this safe today." DiscordConfigService serializes every writer's
    // read-modify-write inside one gate, so the interleave that comment feared — two checkbox
    // clicks in quick succession, or a row mute landing mid-save — composes instead of racing.
    // This window reads Current per use and repaints on Changed: a view, not a snapshot.
    private DiscordConfig CurrentDiscordConfig => _discordConfigService.Current;

    // Set in OnLoaded when DiscordPresence is available, so OnDiscordStatusChanged and OnClosed
    // (subscribe/unsubscribe) both have a reference without re-touching MainViewModel each time.
    private DiscordPresenceService? _discordPresence;

    public SettingsPage(
        IAppSettings settings,
        IStartupRegistration startupRegistration,
        IThemeStore themeStore,
        ThemeService themeService,
        IAccountStore accountStore,
        IAccountTransport transport,
        MainViewModel mainViewModel,
        DiscordConfigService discordConfigService,
        AlertDispatcher alertDispatcher,
        DiscordWebhookSender webhookSender,
        WebhookProbe webhookProbe,
        ROROROblox.Core.Notify.PhoneNotifyConfigService phoneNotifyService,
        ROROROblox.App.Notify.PhoneAlertSender phoneAlertSender,
        ISystemMemoryProbe systemMemoryProbe)
    {
        _automaticMemory = new AutomaticMemorySummary(systemMemoryProbe);
        _alertDispatcher = alertDispatcher;
        _webhookSender = webhookSender;
        _webhookProbe = webhookProbe;
        _phoneNotifyService = phoneNotifyService;
        _phoneAlertSender = phoneAlertSender;
        _settings = settings;
        _startupRegistration = startupRegistration;
        _themeStore = themeStore;
        _themeService = themeService;
        _accountStore = accountStore;
        _transport = transport;
        _mainViewModel = mainViewModel;
        _discordConfigService = discordConfigService;
        InitializeComponent();

        // F-102. Two-way binding, NOT a Click handler. TogglePattern.Toggle() — the only pattern a
        // CheckBox exposes, and the one every assistive technology and automation path uses — goes
        // through WPF's ToggleButtonAutomationPeer, which raises Checked/Unchecked and never Click.
        // So the old handler never ran for those callers: IsChecked flipped, UIA reported On, and
        // streamer mode stayed OFF. A privacy control reporting engaged while disengaged, observed
        // end to end in the Store capture run — toggled via UIA, read back On, and the captured PNG
        // showed real account names.
        //
        // Binding is immune to the whole Click-versus-programmatic split, including automation
        // paths nobody has thought of yet, which is why it beats moving the handler to
        // Checked/Unchecked.
        StreamerModeToggle.SetBinding(
            System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
            new System.Windows.Data.Binding(nameof(MainViewModel.StreamerModeOn))
            {
                Source = _mainViewModel,
                Mode = System.Windows.Data.BindingMode.TwoWay,
            });

        Loaded += OnLoaded;
    }

    /// <summary>
    /// Unsubscribes <see cref="OnDiscordStatusChanged"/> from the presence service. Without this,
    /// every Preferences open/close leaks one subscriber onto <see cref="DiscordPresenceService"/>
    /// — a singleton that outlives this (transient, per-open) window — and each leaked subscriber
    /// keeps firing (and marshalling through this closed window's <see cref="Window.Dispatcher"/>)
    /// for the rest of the process.
    /// </summary>
    public void Dispose()
    {
        if (_discordPresence is { } presence)
        {
            presence.StatusChanged -= OnDiscordStatusChanged;
        }

        // Same leak shape as the presence subscription above: this window is transient per-open,
        // MainViewModel is a singleton, so an unsubscribed handler would fire through a closed
        // window's Dispatcher for the rest of the process.
        _mainViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        // And the config owner outlives this window the same way.
        _discordConfigService.Changed -= OnDiscordConfigChanged;
    }

    /// <summary>
    /// Repaint the Discord controls from a published change, whoever wrote it — this window's own
    /// saves included, and the row context menu's mute, which can now land while this window is
    /// open. Queued rather than inlined: the owner raises inside its write gate, possibly off the
    /// UI thread, and a change raised by this window's own save must not re-enter the handler that
    /// saved it.
    /// <para>
    /// Deliberately does NOT repaint the webhook fields: a repaint mid-edit would stomp a URL being
    /// typed, and the only out-of-window writer today touches <c>MutedAccountIds</c>. If a second
    /// out-of-window writer ever edits webhooks, this is the line to revisit.
    /// </para>
    /// </summary>
    private void OnDiscordConfigChanged(object? sender, DiscordConfig config)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _suppressClickHandlers = true;
            try
            {
                DiscordPresenceToggle.IsChecked = config.PresenceEnabled;
                DiscordJoinToggle.IsChecked = config.JoinEnabled;
                DiscordJoinToggle.IsEnabled = config.PresenceEnabled;
                SetRoutingChecks(config);
                RefreshAlertsStatus();
                RefreshMutedAccounts();
            }
            finally
            {
                _suppressClickHandlers = false;
            }
        });
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
            // v1.18 — the mirror of the Squad Launch modal's careful-mode toggle (F-020). Read on
            // every open, exactly as SquadLaunchWindow.OnLoaded reads it, so the two surfaces agree
            // without either one holding a copy of the value.
            CarefulSquadLaunchToggle.IsChecked = await _settings.GetCarefulSquadLaunchAsync();

            AlwaysShowRecycleToggle.IsChecked = await _settings.GetAlwaysShowRecycleAsync();
            AutoForceStopToggle.IsChecked = await _settings.GetAutoForceStopAsync();

            // Streamer mode reads through to IStreamerIdentityProvider via the view model — there is
            // no separate persisted flag here, which is why this reads the VM rather than _settings.
            // The SUBSCRIPTION is the load-bearing half: see OnViewModelPropertyChanged.
            _mainViewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Discord presence + Join (v1.14+ plan). DiscordPresence is only non-null when
            // App.OnStartup found a non-empty Discord:ApplicationId in appsettings.json — see
            // App.WireDiscordPresenceAsync. Null means the feature can never work in this build,
            // so the toggles are disabled rather than left checkable-but-inert.
            //
            // Fix round 1, Finding 3: this block has its own try/catch, separate from the outer
            // one (which has none — an async void Loaded handler with no catch takes the whole
            // app down). The store underneath InitializeAsync only maps CryptographicException/
            // JsonException to defaults; a locked or ACL-blocked discord.dat throws IOException/
            // UnauthorizedAccessException straight through, and this block runs BEFORE idle +
            // theme population below — an unguarded throw here would blank the rest of the dialog,
            // not just the Discord section.
            try
            {
                await _discordConfigService.InitializeAsync();
                // The phone owner gets the same self-heal: WireAlertsAsync's shared try/catch can
                // skip the phone load when the Discord init throws first, and nothing else ever
                // re-initializes it — without this, the phone section would paint defaults over a
                // populated notify.dat for the rest of the session (review 2026-09-04).
                await _phoneNotifyService.InitializeAsync();
                var discordConfig = CurrentDiscordConfig;
                DiscordPresenceToggle.IsChecked = discordConfig.PresenceEnabled;
                DiscordJoinToggle.IsChecked = discordConfig.JoinEnabled;
                // FIX 7 (final whole-branch review, 2026-08-03): Join has no effect while presence
                // is off (DiscordPresenceService.JoinEnabled is now PresenceEnabled && JoinEnabled)
                // — disabling the checkbox here says so instead of leaving it checkable-but-inert.
                DiscordJoinToggle.IsEnabled = discordConfig.PresenceEnabled;
                // Alerts read the same owner — and, unlike presence, work with no Discord
                // application id, so they are populated outside the DiscordPresence null-check below.
                PopulateAlertControls();
                // A view, not a snapshot: the row context menu can now mute an account while this
                // window is open (it goes through the same owner), and this window has to show it.
                // Unsubscribed in OnClosed, same leak discipline as the presence subscription.
                _discordConfigService.Changed += OnDiscordConfigChanged;
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
            // v1.18 item 7 (F-026). A theme file the store could not read used to just not appear.
            // Reported here rather than at startup because this is the page that owns the picker
            // the file was supposed to show up in, and because a report nobody is looking at is
            // the same silence in a different place.
            ReportThemeFolder(themes);
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
            // v1.18 item 7 (F-019). SetActiveAsync has always applied the theme whether or not the
            // write succeeded, and used to return nothing — so the failure looked exactly like a
            // success and the old theme was back after a restart. The apply is unchanged; the
            // silence is not.
            var change = await _themeService.SetActiveAsync(picked.Id);
            var line = ThemeStatusSummary.ForThemeChange(picked.Name, change);
            // A save that worked does not blank the line, it hands it back to the folder report —
            // see _themeFolderStatus. Painted BEFORE the edge dialog so the message is on screen
            // behind it rather than arriving after the user has finished with the modal.
            ShowThemeStatus(line.Any ? line : _themeFolderStatus);
            // Switching TO a user theme whose edge had to be raised is the same question startup
            // asks — put here too, or the only way to see it would be to restart.
            await EdgeRemediationWindow.AskIfPendingAsync(_themeService, Window.GetWindow(this));
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Recomputes what the themes folder has to say and puts it on the line.
    /// <para>
    /// The file names come from here rather than from the store because
    /// <c>spec.md &gt; §1</c> puts <c>ROROROblox.Core</c> out of this cycle's reach and
    /// <c>IThemeStore</c> reports no failures. <see cref="ThemeStatusSummary.ForFolder"/> carries
    /// the argument for why reading the folder from out here is sound rather than a guess, and the
    /// test that stops the one duplicated rule drifting.
    /// </para>
    /// </summary>
    private void ReportThemeFolder(IReadOnlyList<Theme> loaded)
    {
        _themeFolderStatus = ThemeStatusSummary.ForFolder(loaded, ThemeFolderFileNames());
        ShowThemeStatus(_themeFolderStatus);
    }

    /// <summary>
    /// The <c>*.json</c> names in the user themes folder, matching <c>ThemeStore.ListAsync</c>'s
    /// own enumeration exactly — same pattern, same top-directory-only scope. A folder that cannot
    /// be enumerated yields nothing rather than throwing: the store already returned its built-ins
    /// from the same folder without complaint, so a failure here says nothing new and must not take
    /// down an <c>async void</c> Loaded handler.
    /// </summary>
    private IReadOnlyList<string> ThemeFolderFileNames()
    {
        try
        {
            return System.IO.Directory
                .EnumerateFiles(_themeStore.UserThemesFolder, "*.json", System.IO.SearchOption.TopDirectoryOnly)
                .Select(System.IO.Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void ShowThemeStatus(ThemeStatusSummary.Line line)
    {
        ThemeStatusLine.Text = line.Text;
        ThemeStatusLine.Visibility = line.Any ? Visibility.Visible : Visibility.Collapsed;
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
        var builder = new ThemeBuilderWindow(_themeStore, _themeService) { Owner = Window.GetWindow(this) };
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
                // Recomputed, and it is not busywork: naming a built-in theme in the builder
                // writes a file whose id the built-in already owns, so the save "succeeds",
                // ListAsync drops it and `selected` above lands null. That was silent too.
                ReportThemeFolder(themes);
            }
            finally
            {
                _suppressClickHandlers = false;
            }

            // Asked here rather than inside the builder: the builder is closing at the moment it
            // applies the theme, and a dialog parented to a window on its way out is a flicker.
            await EdgeRemediationWindow.AskIfPendingAsync(_themeService, Window.GetWindow(this));
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
            MessageBox.Show(Window.GetWindow(this),
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
            MessageBox.Show(Window.GetWindow(this),
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
            MessageBox.Show(Window.GetWindow(this),
                $"Couldn't save preference: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _suppressClickHandlers = true;
            CarefulSquadLaunchToggle.IsChecked = await _settings.GetCarefulSquadLaunchAsync();
            _suppressClickHandlers = false;
        }
    }

    private async void OnAutoForceStopToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        var wanted = AutoForceStopToggle.IsChecked == true;
        try
        {
            // No view-model push needed, unlike the Recycle toggle: StopAccountAsync reads this
            // per stop rather than caching it, so the next press already has the new answer.
            await _settings.SetAutoForceStopAsync(wanted);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
                $"Could not save that setting: {ex.Message}",
                "RoRoRo", MessageBoxButton.OK, MessageBoxImage.Warning);
            _suppressClickHandlers = true;
            AutoForceStopToggle.IsChecked = !wanted;
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
            MessageBox.Show(Window.GetWindow(this),
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
    /// "Show what you're playing on Discord." The mutation goes through the owner, and the owner's
    /// <c>Changed</c> event is what pushes it into the live presence service (App wires that
    /// subscription) — this handler no longer calls <c>ApplyAsync</c> itself, so a writer that
    /// isn't this window reaches presence exactly the same way. The status line is NOT read here
    /// either — see <see cref="OnDiscordStatusChanged"/>'s remarks for why a one-time read would
    /// show a stale value; the subscription set up in <see cref="OnLoaded"/> is what keeps
    /// <see cref="DiscordStatusLine"/> correct through both the immediate transient and whatever
    /// Lachee reports afterward.
    /// </summary>
    private async void OnDiscordPresenceToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        var wanted = DiscordPresenceToggle.IsChecked == true;
        // FIX 7: keep the Join checkbox's enabled state tracking presence live, not just at
        // OnLoaded — Join has no effect while presence is off (DiscordPresenceService.JoinEnabled).
        DiscordJoinToggle.IsEnabled = wanted;
        try
        {
            await _discordConfigService.MutateAsync(c => c with { PresenceEnabled = wanted });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
                $"Couldn't save preference: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            // A failed mutate publishes nothing, so Current still matches the disk — repaint from it.
            _suppressClickHandlers = true;
            DiscordPresenceToggle.IsChecked = CurrentDiscordConfig.PresenceEnabled;
            DiscordJoinToggle.IsEnabled = CurrentDiscordConfig.PresenceEnabled;
            _suppressClickHandlers = false;
        }
    }

    /// <summary>"Let friends join your server from Discord." Same shape as <see cref="OnDiscordPresenceToggle"/>.</summary>
    private async void OnDiscordJoinToggle(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;
        var wanted = DiscordJoinToggle.IsChecked == true;
        try
        {
            await _discordConfigService.MutateAsync(c => c with { JoinEnabled = wanted });
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
                $"Couldn't save preference: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _suppressClickHandlers = true;
            DiscordJoinToggle.IsChecked = CurrentDiscordConfig.JoinEnabled;
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
        var discordConfig = CurrentDiscordConfig;
        SetRoutingChecks(discordConfig);
        ShowWebhookMasked(MineWebhookInput, MineWebhookReveal, discordConfig.MineWebhookUrl);
        ShowWebhookMasked(ClanWebhookInput, ClanWebhookReveal, discordConfig.ClanWebhookUrl);
        PopulatePhoneControls();
        RefreshAlertsStatus();
        // Called from HERE and nowhere earlier, on purpose. The count itself reads only the view
        // model's rows and would survive a failed config load — but Unmute all writes the whole
        // DiscordConfig record, and on the failed-InitializeAsync path the owner may still hold
        // startup's read rather than what is on disk now. Offering the button there would let one
        // click wipe somebody's webhook URLs. Inside this method it can only appear once the
        // fresh config is loaded.
        RefreshMutedAccounts();

        // Best-effort, fire-and-forget: name the channel each saved webhook posts to, so a clan
        // webhook sitting in the personal slot is visible on open rather than after it matters.
        if (!string.IsNullOrWhiteSpace(discordConfig.MineWebhookUrl))
        {
            _ = ProbeWebhookAsync(discordConfig.MineWebhookUrl, isClan: false);
        }

        if (!string.IsNullOrWhiteSpace(discordConfig.ClanWebhookUrl))
        {
            _ = ProbeWebhookAsync(discordConfig.ClanWebhookUrl, isClan: true);
        }
    }

    /// <summary>Paint the routing checkboxes from the EFFECTIVE sets — <c>DestinationsFor</c>
    /// migrates a pre-fanout blob's singular field on read, so an old config shows its one
    /// destination ticked rather than everything off.</summary>
    private void SetRoutingChecks(DiscordConfig config)
    {
        var dropped = config.DestinationsFor(AlertKind.AccountDroppedOut);
        var memory = config.DestinationsFor(AlertKind.MemoryWarning);
        DroppedOutLocalCheck.IsChecked = dropped.Contains(AlertDestination.Local);
        DroppedOutMineCheck.IsChecked = dropped.Contains(AlertDestination.Mine);
        DroppedOutClanCheck.IsChecked = dropped.Contains(AlertDestination.Clan);
        DroppedOutPhoneCheck.IsChecked = dropped.Contains(AlertDestination.Phone);
        MemoryWarningLocalCheck.IsChecked = memory.Contains(AlertDestination.Local);
        MemoryWarningMineCheck.IsChecked = memory.Contains(AlertDestination.Mine);
        MemoryWarningClanCheck.IsChecked = memory.Contains(AlertDestination.Clan);
        MemoryWarningPhoneCheck.IsChecked = memory.Contains(AlertDestination.Phone);
    }

    private IReadOnlyList<AlertDestination> ReadChecks(bool droppedOut)
    {
        var boxes = droppedOut
            ? new (System.Windows.Controls.CheckBox Box, AlertDestination Destination)[]
            {
                (DroppedOutLocalCheck, AlertDestination.Local),
                (DroppedOutMineCheck, AlertDestination.Mine),
                (DroppedOutClanCheck, AlertDestination.Clan),
                (DroppedOutPhoneCheck, AlertDestination.Phone),
            }
            : new (System.Windows.Controls.CheckBox Box, AlertDestination Destination)[]
            {
                (MemoryWarningLocalCheck, AlertDestination.Local),
                (MemoryWarningMineCheck, AlertDestination.Mine),
                (MemoryWarningClanCheck, AlertDestination.Clan),
                (MemoryWarningPhoneCheck, AlertDestination.Phone),
            };
        return boxes.Where(b => b.Box.IsChecked == true).Select(b => b.Destination).ToList();
    }

    /// <summary>
    /// The status line is the feature's honesty, so it is recomputed after every change rather
    /// than set once. <see cref="AlertStatusLine"/> owns which sentence belongs to which state —
    /// see its remarks for why that decision does not live here.
    /// </summary>
    /// <summary>
    /// Persist a settings change through the owner, which makes it live immediately — the owner's
    /// <c>Current</c> is what <see cref="AlertDispatcher"/> reads on every dispatch. Before the
    /// owner existed the dispatcher's cache was refreshed only when this dialog closed. That meant
    /// a user who set a destination and then sat watching for an alert with Settings still open got
    /// nothing — the dispatcher was still reading the config from app startup. Measured live:
    /// webhook saved 00:07:26, a real memory crossing at 00:08:46 logged "routed nowhere." A
    /// setting that does not take effect until you close the window it lives in is
    /// indistinguishable from a broken feature.
    /// <para>
    /// The mutation runs inside the owner's gate, possibly off the UI thread — callers capture
    /// control state into locals FIRST and close over values, never over controls.
    /// </para>
    /// </summary>
    private Task SaveDiscordConfigAsync(Func<DiscordConfig, DiscordConfig> mutate)
        => _discordConfigService.MutateAsync(mutate);

    private void RefreshAlertsStatus()
    {
        var phone = _phoneNotifyService.Current;
        var line = AlertStatusLine.Compose(
            CurrentDiscordConfig,
            _alertDispatcher.MineWebhookRejected,
            _alertDispatcher.ClanWebhookRejected,
            _mineChannelName,
            _clanChannelName,
            _alertDispatcher.PhoneRejected,
            phone.IsConfigured,
            phone.Provider switch
            {
                ROROROblox.Core.Notify.PhoneProvider.Pushover => "Pushover",
                ROROROblox.Core.Notify.PhoneProvider.Ntfy => "ntfy",
                _ => null,
            });

        // The glyph is the view's, not the composer's — same rule MainWindow.xaml records for the
        // compat banner. The Tag drives the brush from the Style so the colour stays in markup where
        // the theme gates can see it (F-094, F-098).
        AlertsStatusLine.Text = line.IsFailure ? $"▲ {line.Text}" : line.Text;
        AlertsStatusLine.Tag = line.IsFailure ? "failure" : null;
    }

    // ---------- Phone push (spec 2026-09-04) ----------

    private ROROROblox.Core.Notify.PhoneNotifyConfig CurrentPhoneConfig => _phoneNotifyService.Current;

    /// <summary>Paint the phone controls from the loaded config. Called from
    /// <see cref="PopulateAlertControls"/>, inside the same suppression guard.</summary>
    private void PopulatePhoneControls()
    {
        var phone = CurrentPhoneConfig;
        foreach (var item in PhoneProviderPicker.Items.OfType<System.Windows.Controls.ComboBoxItem>())
        {
            if (Equals(item.Tag as string, phone.Provider.ToString()))
            {
                PhoneProviderPicker.SelectedItem = item;
                break;
            }
        }

        UpdatePhonePanels(phone.Provider);
        ShowWebhookMasked(PushoverUserKeyInput, PushoverUserKeyReveal, phone.PushoverUserKey);
        ShowWebhookMasked(PushoverAppTokenInput, PushoverAppTokenReveal, phone.PushoverAppToken);
        ShowWebhookMasked(NtfyTopicInput, NtfyTopicReveal, phone.NtfyTopic);
        // The reveal helper unlocks fields for editing; the topic is generated, never hand-edited.
        NtfyTopicInput.IsReadOnly = true;
        NtfyServerInput.Text = phone.NtfyServerUrl;
    }

    private void UpdatePhonePanels(ROROROblox.Core.Notify.PhoneProvider provider)
    {
        PushoverFields.Visibility = provider == ROROROblox.Core.Notify.PhoneProvider.Pushover
            ? Visibility.Visible : Visibility.Collapsed;
        NtfyFields.Visibility = provider == ROROROblox.Core.Notify.PhoneProvider.Ntfy
            ? Visibility.Visible : Visibility.Collapsed;
        PhoneTestButton.Visibility = provider == ROROROblox.Core.Notify.PhoneProvider.None
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnPhoneProviderChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressClickHandlers) return;

        var provider = PhoneProviderPicker.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string tag }
            && Enum.TryParse<ROROROblox.Core.Notify.PhoneProvider>(tag, out var parsed)
                ? parsed
                : ROROROblox.Core.Notify.PhoneProvider.None;

        // A different provider is a different endpoint whose credentials were never rejected —
        // without this, a Pushover 401 latched earlier silently killed a switch to ntfy whose
        // topic was already saved in an earlier session (review 2026-09-04).
        _alertDispatcher.ResetPhoneRejection();

        try
        {
            await _phoneNotifyService.MutateAsync(c => c with { Provider = provider });
            UpdatePhonePanels(provider);
            RefreshAlertsStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't save the phone setting: {ex.Message}",
                "Preferences", MessageBoxButton.OK, MessageBoxImage.Warning);

            // Persist failed: put the picker back on what notify.dat actually holds, so the UI
            // cannot show a provider the saved config does not have. Sibling handlers paint only
            // after the persist; this one now does too (review 2026-09-04).
            _suppressClickHandlers = true;
            foreach (var item in PhoneProviderPicker.Items.OfType<System.Windows.Controls.ComboBoxItem>())
            {
                if (Equals(item.Tag as string, CurrentPhoneConfig.Provider.ToString()))
                {
                    PhoneProviderPicker.SelectedItem = item;
                    break;
                }
            }
            _suppressClickHandlers = false;
            UpdatePhonePanels(CurrentPhoneConfig.Provider);
        }
    }

    /// <summary>Show / Hide on a saved phone credential — <see cref="OnRevealWebhookToggled"/>'s
    /// contract (Checked/Unchecked, not Click; see that handler for why), over three fields.</summary>
    private void OnRevealPhoneToggled(object sender, RoutedEventArgs e)
    {
        if (_syncingWebhookReveal) return;

        var (input, reveal, saved) = PhoneFieldFor(sender);
        if (reveal.IsChecked == true)
        {
            input.Text = saved ?? "";
            // The topic stays read-only even revealed — it is generated, and a hand-edited topic
            // is a weaker secret. The Pushover fields unlock like the webhook fields do.
            input.IsReadOnly = ReferenceEquals(input, NtfyTopicInput);
            reveal.Content = "Hide";
            input.Focus();
            input.SelectAll();
        }
        else
        {
            ShowWebhookMasked(input, reveal, saved);
            NtfyTopicInput.IsReadOnly = true;
        }
    }

    private (System.Windows.Controls.TextBox Input, System.Windows.Controls.Primitives.ToggleButton Reveal, string? Saved)
        PhoneFieldFor(object sender)
    {
        if (ReferenceEquals(sender, PushoverAppTokenReveal) || ReferenceEquals(sender, PushoverAppTokenInput))
        {
            return (PushoverAppTokenInput, PushoverAppTokenReveal, CurrentPhoneConfig.PushoverAppToken);
        }

        if (ReferenceEquals(sender, NtfyTopicReveal) || ReferenceEquals(sender, NtfyTopicInput))
        {
            return (NtfyTopicInput, NtfyTopicReveal, CurrentPhoneConfig.NtfyTopic);
        }

        if (ReferenceEquals(sender, PushoverUserKeyReveal) || ReferenceEquals(sender, PushoverUserKeyInput))
        {
            return (PushoverUserKeyInput, PushoverUserKeyReveal, CurrentPhoneConfig.PushoverUserKey);
        }

        // Loud, not silent: a catch-all here once mapped ANY unrecognised sender to the Pushover
        // user key, so a future fourth field wired to the shared handlers without extending this
        // map would have revealed — and could then save over — the wrong credential (review
        // 2026-09-04). A throw fails on the first dev click instead.
        throw new ArgumentException(
            "Unmapped phone credential control — extend PhoneFieldFor before wiring a new field to the shared handlers.",
            nameof(sender));
    }

    /// <summary>One commit handler for both Pushover fields — same anti-drift reasoning as
    /// <see cref="OnWebhookCommitted"/>, and the same mask-is-not-an-edit and
    /// wrong-paste-leaves-saved-value rules.</summary>
    private async void OnPushoverKeyCommitted(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;

        var isToken = ReferenceEquals(sender, PushoverAppTokenInput);
        var (input, reveal, saved) = PhoneFieldFor(sender);

        if (WebhookUrlMasker.IsMasked(input.Text)) return;

        var verdict = ROROROblox.Core.Notify.PhoneCredentialValidator.InspectPushoverKey(
            input.Text, isToken ? "application token" : "user key");
        PhonePushoverVerdict.Text = verdict.Message;

        if (verdict.Kind is not (ROROROblox.Core.Notify.PhoneCredentialKind.Valid
            or ROROROblox.Core.Notify.PhoneCredentialKind.Empty))
        {
            return;
        }

        var value = verdict.Kind == ROROROblox.Core.Notify.PhoneCredentialKind.Valid ? verdict.Normalized : null;
        if (value == saved) return;

        // Fresh credentials get a fresh chance, same as a newly pasted webhook.
        _alertDispatcher.ResetPhoneRejection();
        ShowWebhookMasked(input, reveal, value);

        try
        {
            await _phoneNotifyService.MutateAsync(c => isToken
                ? c with { PushoverAppToken = value }
                : c with { PushoverUserKey = value });
            RefreshAlertsStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't save the key: {ex.Message}",
                "Preferences", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnGenerateNtfyTopicClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CurrentPhoneConfig.NtfyTopic))
        {
            var answer = MessageBox.Show(Window.GetWindow(this),
                "A new topic disconnects your phone until you subscribe to the new one in the ntfy app. Make a new topic?",
                "Preferences", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) return;
        }

        var topic = ROROROblox.Core.Notify.NtfyTopicGenerator.NewTopic();
        _alertDispatcher.ResetPhoneRejection();

        try
        {
            await _phoneNotifyService.MutateAsync(c => c with { NtfyTopic = topic });

            // Repaint through the helper FIRST: it restores the Show/Hide toggle's visibility
            // (Collapsed until a first topic exists) and forces IsChecked to false, so the
            // assignment below is a real false-to-true transition whose Checked handler paints
            // the NEW topic. A bare IsChecked=true was a no-op while the field was already
            // revealed — the OLD topic stayed on screen under a "subscribe to this exact topic"
            // instruction, and on a first-ever topic the Hide button stayed Collapsed over a
            // revealed bearer credential (review 2026-09-04).
            ShowWebhookMasked(NtfyTopicInput, NtfyTopicReveal, topic);
            NtfyTopicInput.IsReadOnly = true;

            // Revealed on purpose: the user's next act is subscribing to this exact string on
            // their phone. Hide puts it back behind the mask.
            NtfyTopicReveal.IsChecked = true;
            PhoneNtfyVerdict.Text = "Subscribe to this exact topic in the ntfy app on your phone, then hit Test my phone.";
            RefreshAlertsStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't save the new topic: {ex.Message}",
                "Preferences", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnNtfyServerCommitted(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;

        var verdict = ROROROblox.Core.Notify.PhoneCredentialValidator.InspectNtfyServer(NtfyServerInput.Text);
        PhoneNtfyVerdict.Text = verdict.Message;

        // Empty restores the default rather than saving a blank — a blank server is not a choice,
        // it is an accident on the way to one.
        var value = verdict.Kind switch
        {
            ROROROblox.Core.Notify.PhoneCredentialKind.Valid => verdict.Normalized!,
            ROROROblox.Core.Notify.PhoneCredentialKind.Empty => "https://ntfy.sh",
            _ => null,
        };
        if (value is null) return;

        // Repaint BEFORE the no-change return: clearing the field back to the default must put
        // the default on screen even when the saved value already is the default — the early
        // return used to leave a blank box over a configured server (review 2026-09-04).
        NtfyServerInput.Text = value;
        if (value == CurrentPhoneConfig.NtfyServerUrl) return;

        _alertDispatcher.ResetPhoneRejection();

        try
        {
            await _phoneNotifyService.MutateAsync(c => c with { NtfyServerUrl = value });
            RefreshAlertsStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't save the server: {ex.Message}",
                "Preferences", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Sends a real notification down the real path, like <see cref="OnSendTestClick"/> — "it
    /// says configured but nothing buzzes" is the failure that otherwise surfaces at 3am. A test
    /// is a MemoryWarning-kind send on purpose: it must not carry the drop's quiet-hours-bypass
    /// priority.
    /// </summary>
    private async void OnPhoneTestClick(object sender, RoutedEventArgs e)
    {
        var phone = CurrentPhoneConfig;
        if (!phone.IsConfigured)
        {
            AlertsStatusLine.Text = "Finish the phone setup above first — there's nowhere to send a test yet.";
            AlertsStatusLine.Tag = null;
            return;
        }

        PhoneTestButton.IsEnabled = false;
        AlertsStatusLine.Text = "Sending…";
        AlertsStatusLine.Tag = null;
        try
        {
            var result = await _phoneAlertSender.SendAsync(phone, AlertKind.MemoryWarning,
                new WebhookPayload("RoRoRo test", "If your phone buzzed, phone alerts work."));

            if (result == ROROROblox.App.Notify.PhoneSendResult.Sent)
            {
                // A delivered test is proof the saved credentials work — clear any stale session
                // latch so real alerts flow again without a re-paste. Unlike a 404'd webhook, a
                // latched phone rejection CAN coexist with working credentials (a transient 403
                // from a self-hosted server, the pre-cap oversize 400), so the passing test is
                // the honest reset signal (review 2026-09-04).
                _alertDispatcher.ResetPhoneRejection();
            }

            AlertsStatusLine.Text = result switch
            {
                ROROROblox.App.Notify.PhoneSendResult.Sent =>
                    "Sent — your phone should buzz within a few seconds.",
                ROROROblox.App.Notify.PhoneSendResult.EndpointRejected =>
                    phone.Provider == ROROROblox.Core.Notify.PhoneProvider.Pushover
                        ? "Pushover rejected the saved keys — re-check both on pushover.net and paste them again."
                        : "The ntfy server refused that topic — generate a new one and re-subscribe on your phone.",
                ROROROblox.App.Notify.PhoneSendResult.RateLimited =>
                    "The push service is rate-limiting us. Wait a minute; the setup itself looks fine.",
                _ => "Couldn't reach the push service.",
            };

            // Text and colour must agree: the style paints Tag="failure" red and everything else
            // cyan, and this handler writes Text without going through RefreshAlertsStatus — a
            // stale failure Tag was painting "Sent — your phone should buzz" in the warning red
            // (review 2026-09-04).
            AlertsStatusLine.Tag =
                result == ROROROblox.App.Notify.PhoneSendResult.Sent ? null : "failure";
        }
        finally
        {
            PhoneTestButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Pushover's application form has an icon slot; this hands the member the same 128x128
    /// mark Este's own registration uses, so nobody hunts a repo for it (Este, 2026-09-05).
    /// The PNG is embedded from docs/store/graphics — one source of truth.
    /// </summary>
    private void OnSavePushoverIconClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "rororo-icon-128.png",
            Filter = "PNG image|*.png",
            Title = "Save the icon",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            using var resource = typeof(SettingsPage).Assembly
                .GetManifestResourceStream("ROROROblox.App.Notify.pushover-icon-128.png")
                ?? throw new InvalidOperationException("The embedded icon resource is missing.");
            using var file = System.IO.File.Create(dialog.FileName);
            resource.CopyTo(file);
            PhonePushoverVerdict.Text = "Icon saved — upload it in the icon slot on pushover.net's application form.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't save the icon: {ex.Message}",
                "Preferences", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnAlertRoutingChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressClickHandlers) return;

        // Read the checkboxes here, on the UI thread — the mutate lambda may run off it.
        var droppedOut = ReadChecks(droppedOut: true);
        var memoryWarning = ReadChecks(droppedOut: false);

        try
        {
            await SaveDiscordConfigAsync(c => c with
            {
                DroppedOutDestinations = droppedOut,
                MemoryWarningDestinations = memoryWarning,
                // The singular fields are the rollback mirror: an older binary reads only them,
                // and "first ticked destination" beats "silently dropped" — the destination-4
                // hazard the phone spec records.
                DroppedOutDestination = droppedOut.Count > 0 ? droppedOut[0] : AlertDestination.None,
                MemoryWarningDestination = memoryWarning.Count > 0 ? memoryWarning[0] : AlertDestination.None,
            });
            RefreshAlertsStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't save alert routing: {ex.Message}",
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
        var saved = isClan ? CurrentDiscordConfig.ClanWebhookUrl : CurrentDiscordConfig.MineWebhookUrl;

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
        var saved = isClan ? CurrentDiscordConfig.ClanWebhookUrl : CurrentDiscordConfig.MineWebhookUrl;

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
        if (isClan)
        {
            _clanChannelName = null;
            _alertDispatcher.ResetClanRejection();
        }
        else
        {
            _mineChannelName = null;
            _alertDispatcher.ResetMineRejection();
        }

        // Straight back behind the mask once it is saved. Leaving a just-pasted webhook revealed
        // means the credential stays on screen for the rest of the session — the same exposure,
        // just later.
        ShowWebhookMasked(input, isClan ? ClanWebhookReveal : MineWebhookReveal, url);
        RefreshAlertsStatus();

        try
        {
            await SaveDiscordConfigAsync(c => isClan
                ? c with { ClanWebhookUrl = url }
                : c with { MineWebhookUrl = url });
            if (url is not null) await ProbeWebhookAsync(url, isClan);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Couldn't save the webhook: {ex.Message}",
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
        var discordConfig = CurrentDiscordConfig;
        var targets = new List<(string Label, string Url)>();
        if (!string.IsNullOrWhiteSpace(discordConfig.MineWebhookUrl))
        {
            targets.Add(("My channel", discordConfig.MineWebhookUrl));
        }

        if (!string.IsNullOrWhiteSpace(discordConfig.ClanWebhookUrl))
        {
            targets.Add(("Clan channel", discordConfig.ClanWebhookUrl));
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
        MessageBox.Show(Window.GetWindow(this),
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

    // ---------- Muted accounts (v1.18, spec §4.3 — F-024) ----------

    /// <summary>
    /// Paints the muted-account block, or takes it off the page.
    /// <para>
    /// Zero muted collapses the whole block rather than rendering "0 accounts are muted" or leaving
    /// an unmute button over nothing — <c>prd.md &gt; Story 1.3</c> asks for a clean state, and a
    /// count of zero is a control reporting the absence of a thing the user never did.
    /// <see cref="MutedAccountsSummary.Summary.Any"/> is what decides that, so the rule is a
    /// property a test can assert on rather than a comparison living in this handler.
    /// </para>
    /// </summary>
    private void RefreshMutedAccounts()
    {
        var summary = MutedAccountsSummary.Describe(_mainViewModel.Accounts);
        MutedAccountsLine.Text = summary.Text;
        MutedAccountsRow.Visibility = summary.Any ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Unmute everything, in the order that survives a failed write.
    /// <para>
    /// Live rows first, then the file. <see cref="MutedAccountsSummary.UnmuteRows"/> hands back
    /// exactly the rows it changed, so a save that throws is undone by re-muting those and nothing
    /// else — no reconstruction from an id set, and no chance of switching a mute back on for a row
    /// that never had one.
    /// </para>
    /// <para>
    /// The write goes through the owner as ONE mutation rather than through
    /// <c>MainViewModel.SetAlertsMutedAsync</c> per row — not for safety any more (the owner
    /// serializes writers, so per-row calls would compose correctly), but because one write is one
    /// disk round-trip and one <c>Changed</c> event instead of N of each.
    /// </para>
    /// </summary>
    private async void OnUnmuteAllClick(object sender, RoutedEventArgs e)
    {
        var cleared = MutedAccountsSummary.UnmuteRows(_mainViewModel.Accounts);
        RefreshMutedAccounts();

        try
        {
            await SaveDiscordConfigAsync(MutedAccountsSummary.WithoutMutes);
        }
        catch (Exception ex)
        {
            foreach (var row in cleared)
            {
                row.AlertsMuted = true;
            }

            RefreshMutedAccounts();
            MessageBox.Show(Window.GetWindow(this),
                $"Couldn't save that: {ex.Message}",
                "Preferences",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // ---------- The theme prompt, reversible (v1.18, spec §4.3 — F-078) ----------

    /// <summary>
    /// Asks the outline question again, for the theme on screen.
    /// <para>
    /// ONE CONSENT PATH, NOT TWO. This reaches the same dialog through the same
    /// <see cref="EdgeRemediationWindow.AskIfPendingAsync"/> the startup path, the picker and the
    /// theme builder use, so the answer is phrased and recorded exactly once —
    /// <c>ThemeService.AnswerEdgeQuestionAsync</c> into
    /// <c>IAppSettings.SetEdgeRemediationAnswerAsync</c>. All
    /// <see cref="ThemeService.ReopenEdgeQuestion"/> does is make there be a question again; it
    /// writes nothing.
    /// </para>
    /// <para>
    /// Nothing to ask is said out loud rather than left as a dead click. This is a button somebody
    /// can find without having seen the prompt (that is Story 1.4's requirement), so the common case
    /// is pressing it on a built-in theme — and a button that silently does nothing is the defect
    /// this cycle is about wearing a different hat. It is a MessageBox and not the section's status
    /// line because there is no theme status line yet; the one spec §5 describes reports FAILURES,
    /// and "this theme has nothing to choose" is not one.
    /// </para>
    /// </summary>
    private async void OnReviewEdgeClick(object sender, RoutedEventArgs e)
    {
        if (!_themeService.ReopenEdgeQuestion())
        {
            MessageBox.Show(Window.GetWindow(this),
                "There's nothing to choose for this theme.\n\n"
                + "RoRoRo only asks about button outlines on themes people write themselves, and "
                + "only when the outline a theme sets would be too faint to tell a button apart "
                + "from the surface behind it. The built-in themes are ours to get right, so they "
                + "are never asked about.",
                "Button outlines",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await EdgeRemediationWindow.AskIfPendingAsync(_themeService, Window.GetWindow(this));
    }

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
            // Push the change into the live monitor + VM. The tray path used to do this once, on
            // dialog close; a shell page has no close moment, and the main-window path never did
            // it at all — an edit here silently waited for a restart. Per-edit is the fix for
            // both (F-013).
            await _mainViewModel.InitializeIdleSettingsAsync(_settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
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
            // Same per-edit re-push as the mute toggle above (F-013).
            await _mainViewModel.InitializeIdleSettingsAsync(_settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this),
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
                Window.GetWindow(this),
                "You don't have any saved accounts to export yet.",
                "Nothing to export",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var window = new ExportAccountsWindow(_accountStore, _transport, accounts) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private void OnImportAccountsClick(object sender, RoutedEventArgs e)
    {
        var window = new ImportAccountsWindow(_accountStore, _transport, _mainViewModel) { Owner = Window.GetWindow(this) };
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

}
