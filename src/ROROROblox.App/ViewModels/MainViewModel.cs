using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.About;
using ROROROblox.App.Diagnostics;
using ROROROblox.App.Friends;
using ROROROblox.App.Modals;
using ROROROblox.App.Settings;
using ROROROblox.App.SquadLaunch;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.App.ViewModels;

/// <summary>
/// Orchestrates the main-window flows: Add Account, Launch As, Remove, Re-authenticate.
/// Coordinates <see cref="ICookieCapture"/> + <see cref="IRobloxApi"/> + <see cref="IAccountStore"/>
/// + <see cref="IRobloxLauncher"/>; surfaces error modals for the four spec §7 buckets.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ICookieCapture _cookieCapture;
    private readonly IRobloxApi _api;
    private readonly IAccountStore _accountStore;
    private readonly IRobloxLauncher _launcher;
    private readonly IRobloxCompatChecker _compatChecker;
    private readonly IAppSettings _settings;
    private readonly IFavoriteGameStore _favorites;
    private readonly IRobloxProcessTracker _processTracker;
    private readonly IDiagnosticsCollector _diagnostics;
    private readonly IPrivateServerStore _privateServerStore;
    private readonly ILogger<MainViewModel> _log;
    private readonly DispatcherTimer _ticker;

    private string _statusBanner = string.Empty;
    private string? _robloxCompatBanner;
    private bool _isBusy;
    private int _liveProcessCount;

    public MainViewModel(
        ICookieCapture cookieCapture,
        IRobloxApi api,
        IAccountStore accountStore,
        IRobloxLauncher launcher,
        IRobloxCompatChecker compatChecker,
        IAppSettings settings,
        IFavoriteGameStore favorites,
        IRobloxProcessTracker processTracker,
        IDiagnosticsCollector diagnostics,
        IPrivateServerStore privateServerStore,
        ILogger<MainViewModel>? log = null)
    {
        _cookieCapture = cookieCapture;
        _api = api;
        _accountStore = accountStore;
        _launcher = launcher;
        _compatChecker = compatChecker;
        _settings = settings;
        _favorites = favorites;
        _processTracker = processTracker;
        _diagnostics = diagnostics;
        _privateServerStore = privateServerStore;
        _log = log ?? NullLogger<MainViewModel>.Instance;

        AddAccountCommand = new RelayCommand(AddAccountAsync, () => !IsBusy);
        LaunchAccountCommand = new RelayCommand(p => LaunchAccountAsync(p as AccountSummary));
        RemoveAccountCommand = new RelayCommand(p => RemoveAccountAsync(p as AccountSummary));
        ReauthenticateCommand = new RelayCommand(p => ReauthenticateAsync(p as AccountSummary));
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        LaunchAllCommand = new RelayCommand(LaunchAllAsync, () => !IsBusy && Accounts.Any(a => !a.SessionExpired && !a.IsRunning));
        StopAccountCommand = new RelayCommand(p => StopAccount(p as AccountSummary));
        OpenDiagnosticsCommand = new RelayCommand(OpenDiagnostics);
        OpenAboutCommand = new RelayCommand(OpenAbout);
        OpenSquadLaunchCommand = new RelayCommand(OpenSquadLaunchAsync, () => !IsBusy && Accounts.Count > 0);
        OpenFriendFollowCommand = new RelayCommand(p => OpenFriendFollowAsync(p as AccountSummary));

        _processTracker.ProcessAttached += OnProcessAttached;
        _processTracker.ProcessExited += OnProcessExited;
        _processTracker.ProcessAttachFailed += OnProcessAttachFailed;

        // Tick once a minute to keep "5 min ago" / "Running for 12 min" current.
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _ticker.Tick += (_, _) =>
        {
            foreach (var summary in Accounts)
            {
                summary.RefreshRelativeTimes();
            }
        };
        _ticker.Start();
    }

    public ObservableCollection<AccountSummary> Accounts { get; } = [];

    /// <summary>
    /// Games available for the per-account picker on each row. Synced from the favorites store
    /// at LoadAsync time and again every time the Games dialog closes.
    /// </summary>
    public ObservableCollection<FavoriteGame> AvailableGames { get; } = [];

    public ICommand AddAccountCommand { get; }
    public ICommand LaunchAccountCommand { get; }
    public ICommand RemoveAccountCommand { get; }
    public ICommand ReauthenticateCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand LaunchAllCommand { get; }
    public ICommand StopAccountCommand { get; }
    public ICommand OpenDiagnosticsCommand { get; }
    public ICommand OpenAboutCommand { get; }
    public ICommand OpenSquadLaunchCommand { get; }
    public ICommand OpenFriendFollowCommand { get; }

    /// <summary>How many tracked Roblox client processes are currently alive.</summary>
    public int LiveProcessCount
    {
        get => _liveProcessCount;
        private set
        {
            if (SetField(ref _liveProcessCount, value))
            {
                OnPropertyChanged(nameof(LiveProcessSummary));
            }
        }
    }

    /// <summary>Footer text — e.g. "3 Roblox clients running" / "No clients running".</summary>
    public string LiveProcessSummary => _liveProcessCount switch
    {
        0 => "No Roblox clients running",
        1 => "1 Roblox client running",
        _ => $"{_liveProcessCount} Roblox clients running",
    };

    public string StatusBanner
    {
        get => _statusBanner;
        set => SetField(ref _statusBanner, value);
    }

    /// <summary>
    /// Yellow drift banner — populated when the installed Roblox version is outside the remote
    /// known-good range fetched from <c>roblox-compat.json</c>. Null when no drift / fetch failed.
    /// Spec §7.1.
    /// </summary>
    public string? RobloxCompatBanner
    {
        get => _robloxCompatBanner;
        set => SetField(ref _robloxCompatBanner, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    /// <summary>Loads accounts + games from disk. Called once at MainWindow load.</summary>
    public async Task LoadAsync()
    {
        try
        {
            var accounts = await _accountStore.ListAsync();
            Accounts.Clear();
            foreach (var account in accounts.OrderByDescending(a => a.LastLaunchedAt ?? a.CreatedAt))
            {
                Accounts.Add(new AccountSummary(account));
            }
        }
        catch (AccountStoreCorruptException)
        {
            ShowDpapiCorruptModal();
        }

        await ReloadGamesAsync();
    }

    /// <summary>
    /// Reload <see cref="AvailableGames"/> from the favorites store and re-sync each account's
    /// <see cref="AccountSummary.SelectedGame"/> -- preserve current selection if still present,
    /// else fall back to the favorites default. Called on initial load + after the Games dialog
    /// closes (since the user may have added / removed / set-default'd a game).
    /// </summary>
    public async Task ReloadGamesAsync()
    {
        var games = await _favorites.ListAsync();
        AvailableGames.Clear();
        foreach (var game in games)
        {
            AvailableGames.Add(game);
        }

        var defaultGame = AvailableGames.FirstOrDefault(g => g.IsDefault) ?? AvailableGames.FirstOrDefault();
        foreach (var account in Accounts)
        {
            var stillThere = AvailableGames.FirstOrDefault(g => g.PlaceId == account.SelectedGame?.PlaceId);
            account.SelectedGame = stillThere ?? defaultGame;
        }
    }

    /// <summary>
    /// Fires the remote compat fetch + version-drift check. Best-effort; failures leave the
    /// banner null. Called by App.OnStartup after the main window is loaded.
    /// </summary>
    public async Task LoadCompatBannerAsync()
    {
        try
        {
            var result = await _compatChecker.CheckAsync();
            RobloxCompatBanner = result.HasDrift ? result.Banner : null;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Compat banner check failed; leaving null.");
            RobloxCompatBanner = null;
        }
    }

    /// <summary>
    /// Background pass that validates every saved cookie against Roblox's authenticated-user
    /// endpoint. Marks expired sessions yellow proactively so the user doesn't discover them
    /// only when Launch As fails. Runs sequentially with a 350 ms gap between requests so we
    /// don't hammer auth on startup. Skips accounts already running (their cookie just worked).
    /// </summary>
    public async Task ValidateSessionsAsync(CancellationToken ct = default)
    {
        var snapshot = Accounts.ToList();
        if (snapshot.Count == 0)
        {
            return;
        }
        _log.LogInformation("Validating {Count} stored sessions in background.", snapshot.Count);

        foreach (var summary in snapshot)
        {
            if (ct.IsCancellationRequested) return;
            if (summary.IsRunning) continue;

            string cookie;
            try
            {
                cookie = await _accountStore.RetrieveCookieAsync(summary.Id).ConfigureAwait(true);
            }
            catch (AccountStoreCorruptException)
            {
                // Don't show the modal here — first launch attempt will surface it cleanly.
                _log.LogWarning("AccountStore corrupt during session validation; aborting pass.");
                return;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "RetrieveCookieAsync failed for {AccountId}; skipping.", summary.Id);
                continue;
            }

            try
            {
                var profile = await _api.GetUserProfileAsync(cookie).ConfigureAwait(true);
                summary.SessionExpired = false;
                summary.RobloxUserId = profile.Id; // cache for the Friends modal
            }
            catch (CookieExpiredException)
            {
                _log.LogInformation("Session for {AccountId} ({Name}) is expired.", summary.Id, summary.DisplayName);
                summary.SessionExpired = true;
            }
            catch (Exception ex)
            {
                // Network failure / 5xx — leave the session badge alone. Don't false-alarm yellow
                // on a flaky DNS lookup.
                _log.LogDebug(ex, "Validation transient failure for {AccountId}; leaving state.", summary.Id);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(350), ct).ConfigureAwait(true);
            }
            catch (TaskCanceledException)
            {
                return;
            }
        }

        var expired = snapshot.Count(s => s.SessionExpired);
        if (expired > 0)
        {
            StatusBanner = expired == 1
                ? "1 saved session has expired. Click Re-authenticate to refresh it."
                : $"{expired} saved sessions have expired. Click Re-authenticate to refresh.";
        }
    }

    private async Task AddAccountAsync()
    {
        IsBusy = true;
        try
        {
            var captured = await _cookieCapture.CaptureAsync();
            switch (captured)
            {
                case CookieCaptureResult.Success success:
                    await CompleteAddAsync(success);
                    break;
                case CookieCaptureResult.Cancelled:
                    return;
                case CookieCaptureResult.Failed failed when failed.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase):
                    ShowWebView2NotInstalledModal();
                    break;
                case CookieCaptureResult.Failed failed:
                    StatusBanner = failed.Message;
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompleteAddAsync(CookieCaptureResult.Success captured)
    {
        string avatarUrl = string.Empty;
        try
        {
            avatarUrl = await _api.GetAvatarHeadshotUrlAsync(captured.UserId);
        }
        catch (Exception ex)
        {
            // Avatar fetch is best-effort — the row still works without an image.
            _log.LogDebug(ex, "Avatar fetch failed for new account {UserId}.", captured.UserId);
        }

        var account = await _accountStore.AddAsync(captured.Username, avatarUrl, captured.Cookie);
        var summary = new AccountSummary(account) { RobloxUserId = captured.UserId };
        Accounts.Insert(0, summary);
        _log.LogInformation("Added account {AccountId} ({Username}, userId {UserId})", account.Id, captured.Username, captured.UserId);
        StatusBanner = $"Added {captured.Username}.";
    }

    private async Task LaunchAccountAsync(AccountSummary? summary, LaunchTarget? overrideTarget = null)
    {
        if (summary is null)
        {
            return;
        }

        summary.IsLaunching = true;
        summary.StatusText = "Launching...";
        _log.LogInformation("Launching account {AccountId} ({DisplayName}) target={Target}",
            summary.Id, summary.DisplayName, overrideTarget?.GetType().Name ?? "from-row");
        try
        {
            string cookie;
            try
            {
                cookie = await _accountStore.RetrieveCookieAsync(summary.Id);
            }
            catch (AccountStoreCorruptException)
            {
                ShowDpapiCorruptModal();
                return;
            }

            // Target precedence:
            //   1. Explicit override (Squad Launch / Friend Follow / Join-by-Link).
            //   2. Per-row SelectedGame from the ComboBox.
            //   3. LaunchTarget.DefaultGame -> launcher resolves favorites + settings tier.
            LaunchTarget target;
            if (overrideTarget is not null)
            {
                target = overrideTarget;
            }
            else if (summary.SelectedGame is { PlaceId: > 0 } sg)
            {
                target = new LaunchTarget.Place(sg.PlaceId);
            }
            else
            {
                target = new LaunchTarget.DefaultGame();
            }

            var result = await _launcher.LaunchAsync(cookie, target);
            switch (result)
            {
                case LaunchResult.Started started:
                    await _accountStore.TouchLastLaunchedAsync(summary.Id);
                    summary.StampLaunched(DateTimeOffset.UtcNow);
                    summary.SessionExpired = false;
                    summary.StatusText = string.Empty;
                    summary.LastClosedAtUtc = null;
                    _log.LogInformation("Launcher pid {Pid} for {AccountId}; tracking RobloxPlayerBeta", started.Pid, summary.Id);
                    // Fire-and-forget: tracker watches for the player process. UI updates flow back
                    // through ProcessAttached / ProcessAttachFailed events.
                    _ = _processTracker.TrackLaunchAsync(summary.Id, started.LaunchedAtUtc);
                    break;
                case LaunchResult.CookieExpired:
                    _log.LogInformation("Cookie expired for account {AccountId}", summary.Id);
                    summary.SessionExpired = true;
                    summary.StatusText = string.Empty;
                    break;
                case LaunchResult.Failed failed when failed.Message.Contains("Roblox does not appear to be installed", StringComparison.OrdinalIgnoreCase):
                    _log.LogWarning("Roblox not installed at launch time for account {AccountId}", summary.Id);
                    summary.StatusText = "Roblox not installed.";
                    ShowRobloxNotInstalledModal();
                    break;
                case LaunchResult.Failed failed:
                    _log.LogWarning("Launch failed for account {AccountId}: {Message}", summary.Id, failed.Message);
                    summary.StatusText = failed.Message;
                    break;
            }
        }
        finally
        {
            summary.IsLaunching = false;
        }
    }

    /// <summary>
    /// Launch every non-expired, non-running account in sequence with a 1.5s gap. The gap gives
    /// the tracker time to claim each <c>RobloxPlayerBeta.exe</c> by start time before the next
    /// launch fires (otherwise FIFO matching gets murky).
    /// </summary>
    private async Task LaunchAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusBanner = "Launching all accounts...";
        try
        {
            var targets = Accounts.Where(a => !a.SessionExpired && !a.IsRunning && !a.IsLaunching).ToList();
            _log.LogInformation("LaunchAll: {Count} target accounts", targets.Count);
            if (targets.Count == 0)
            {
                StatusBanner = "Nothing to launch — every account is already running or expired.";
                return;
            }

            var i = 0;
            foreach (var summary in targets)
            {
                StatusBanner = $"Launching {summary.DisplayName} ({++i} of {targets.Count})...";
                await LaunchAccountAsync(summary);
                if (i < targets.Count)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1500));
                }
            }
            StatusBanner = $"Launch all finished. {targets.Count} client{(targets.Count == 1 ? "" : "s")} dispatched.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Open the Squad Launch modal. After the modal closes, if the user picked a target,
    /// dispatch every eligible account into it via <see cref="SquadLaunchAsync"/>.
    /// </summary>
    private async Task OpenSquadLaunchAsync()
    {
        var eligible = Accounts.Count(a => !a.SessionExpired && !a.IsRunning && !a.IsLaunching);
        var running = Accounts.Count(a => a.IsRunning);
        var expired = Accounts.Count(a => a.SessionExpired);

        var window = new SquadLaunchWindow(_privateServerStore, _api, eligible, running, expired)
        {
            Owner = Application.Current.MainWindow,
        };
        var dialogResult = window.ShowDialog();
        if (dialogResult == true && window.SelectedTarget is { } target)
        {
            await SquadLaunchAsync(target);
        }
    }

    /// <summary>
    /// Mass-launch every eligible account into the same private server, throttled 1.5 s apart so
    /// the process tracker can FIFO-claim each <c>RobloxPlayerBeta.exe</c> by start time. The
    /// override target trumps each row's per-account SelectedGame.
    /// </summary>
    private async Task SquadLaunchAsync(LaunchTarget.PrivateServer target)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var targets = Accounts.Where(a => !a.SessionExpired && !a.IsRunning && !a.IsLaunching).ToList();
            _log.LogInformation("SquadLaunch: placeId={PlaceId}, {Count} target accounts",
                target.PlaceId, targets.Count);
            if (targets.Count == 0)
            {
                StatusBanner = "Nothing to launch — every account is already running or expired.";
                return;
            }

            var i = 0;
            foreach (var summary in targets)
            {
                StatusBanner = $"Squad launching {summary.DisplayName} ({++i} of {targets.Count}) into the same private server...";
                await LaunchAccountAsync(summary, overrideTarget: target);
                if (i < targets.Count)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1500));
                }
            }
            StatusBanner = $"Squad launch finished. {targets.Count} client{(targets.Count == 1 ? "" : "s")} dispatched into the same private server.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Open the Friends modal for one account. Resolves the Roblox userId on first open
    /// (cached on <see cref="AccountSummary"/> for subsequent opens). After the modal closes,
    /// if the user picked a friend to follow, fire the launch with that target.
    /// </summary>
    private async Task OpenFriendFollowAsync(AccountSummary? summary)
    {
        if (summary is null) return;

        string cookie;
        try
        {
            cookie = await _accountStore.RetrieveCookieAsync(summary.Id);
        }
        catch (AccountStoreCorruptException)
        {
            ShowDpapiCorruptModal();
            return;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "RetrieveCookieAsync failed for friends modal {AccountId}", summary.Id);
            StatusBanner = "Couldn't read this account's saved session.";
            return;
        }

        // Resolve userId if we don't already have it cached.
        long userId = summary.RobloxUserId ?? 0;
        if (userId <= 0)
        {
            try
            {
                var profile = await _api.GetUserProfileAsync(cookie);
                userId = profile.Id;
                summary.RobloxUserId = userId;
            }
            catch (CookieExpiredException)
            {
                summary.SessionExpired = true;
                StatusBanner = $"{summary.DisplayName}'s session expired — re-authenticate first.";
                return;
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Couldn't resolve userId for friends modal {AccountId}", summary.Id);
                StatusBanner = "Couldn't reach Roblox to load friends. Try again in a moment.";
                return;
            }
        }

        var window = new FriendFollowWindow(_api, cookie, userId, summary.DisplayName)
        {
            Owner = Application.Current.MainWindow,
        };
        if (window.ShowDialog() == true && window.SelectedTarget is { } target)
        {
            await LaunchAccountAsync(summary, overrideTarget: target);
        }
    }

    /// <summary>
    /// Send the tracked Roblox window for this account a graceful close (CloseMainWindow).
    /// Falls back to Kill if a second click arrives while still tracking.
    /// </summary>
    private void StopAccount(AccountSummary? summary)
    {
        if (summary is null || !_processTracker.IsTracking(summary.Id))
        {
            return;
        }
        _log.LogInformation("StopAccount {AccountId} (pid {Pid})", summary.Id, summary.RunningPid);
        if (!_processTracker.RequestClose(summary.Id))
        {
            // Window unresponsive — escalate.
            _processTracker.Kill(summary.Id);
        }
    }

    private void OnProcessAttached(object? sender, RobloxProcessEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (summary is null) return;
            summary.IsRunning = true;
            summary.RunningPid = e.Pid;
            summary.RunningSinceUtc = e.OccurredAtUtc;
            summary.StatusText = string.Empty;
            LiveProcessCount = _processTracker.Attached.Count;
            RelayCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnProcessExited(object? sender, RobloxProcessEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (summary is null) return;
            summary.IsRunning = false;
            summary.RunningPid = null;
            summary.RunningSinceUtc = null;
            summary.LastClosedAtUtc = e.OccurredAtUtc;
            LiveProcessCount = _processTracker.Attached.Count;
            RelayCommand.RaiseCanExecuteChanged();
        });
    }

    private void OnProcessAttachFailed(object? sender, RobloxProcessEventArgs e)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            var summary = Accounts.FirstOrDefault(a => a.Id == e.AccountId);
            if (summary is null) return;
            // The launcher fired but no player process appeared. Most common: Roblox version drift,
            // place removed, antivirus quarantine. Surface the hint in the row.
            summary.StatusText = "Launch never connected. Check Roblox is current + antivirus isn't blocking.";
        });
    }

    private async Task RemoveAccountAsync(AccountSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Remove {summary.DisplayName}?\nYou'll need to log in again to add it back.",
            "Remove Account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await _accountStore.RemoveAsync(summary.Id);
        Accounts.Remove(summary);
        StatusBanner = $"Removed {summary.DisplayName}.";
    }

    private async Task ReauthenticateAsync(AccountSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var captured = await _cookieCapture.CaptureAsync();
            if (captured is not CookieCaptureResult.Success success)
            {
                if (captured is CookieCaptureResult.Failed failed
                    && failed.Message.Contains("WebView2", StringComparison.OrdinalIgnoreCase))
                {
                    ShowWebView2NotInstalledModal();
                }
                return;
            }

            // Refresh avatar URL while we're at it (display name might have changed too — but we
            // keep the original DisplayName so the row identity is stable).
            try
            {
                _ = await _api.GetAvatarHeadshotUrlAsync(success.UserId);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Avatar refresh failed during reauth for {AccountId}.", summary.Id);
            }

            await _accountStore.UpdateCookieAsync(summary.Id, success.Cookie);
            _log.LogInformation("Re-authenticated account {AccountId}", summary.Id);
            summary.SessionExpired = false;
            summary.StatusText = "Re-authenticated.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OpenSettings()
    {
        var window = new SettingsWindow(_favorites, _api) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
        // Refresh in case the user added / removed / set-default'd a game.
        _ = ReloadGamesAsync();
    }

    private void OpenDiagnostics()
    {
        var window = new DiagnosticsWindow(_diagnostics) { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    private void OpenAbout()
    {
        var window = new AboutWindow { Owner = Application.Current.MainWindow };
        window.ShowDialog();
    }

    private static void ShowWebView2NotInstalledModal()
    {
        var window = new WebView2NotInstalledWindow();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    private static void ShowRobloxNotInstalledModal()
    {
        var window = new RobloxNotInstalledWindow();
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    private void ShowDpapiCorruptModal()
    {
        var window = new DpapiCorruptWindow();
        window.Owner = Application.Current.MainWindow;
        var startFresh = window.ShowDialog() == true;
        if (startFresh)
        {
            // The store's load already failed; renaming + creating-empty is the recovery path.
            // For v1 we let the next AddAsync naturally overwrite the corrupt file via the
            // atomic-write path. The accounts list stays empty.
            Accounts.Clear();
            StatusBanner = "Started fresh. Add accounts to begin.";
        }
        else
        {
            // User chose Quit — let the app exit so they can restore from a backup.
            Application.Current.Shutdown(0);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
