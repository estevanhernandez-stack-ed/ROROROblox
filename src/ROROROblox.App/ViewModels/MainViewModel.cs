using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using ROROROblox.App.Modals;
using ROROROblox.App.Settings;
using ROROROblox.Core;

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

    private string _statusBanner = string.Empty;
    private string? _robloxCompatBanner;
    private bool _isBusy;

    public MainViewModel(
        ICookieCapture cookieCapture,
        IRobloxApi api,
        IAccountStore accountStore,
        IRobloxLauncher launcher,
        IRobloxCompatChecker compatChecker,
        IAppSettings settings,
        IFavoriteGameStore favorites)
    {
        _cookieCapture = cookieCapture;
        _api = api;
        _accountStore = accountStore;
        _launcher = launcher;
        _compatChecker = compatChecker;
        _settings = settings;
        _favorites = favorites;

        AddAccountCommand = new RelayCommand(AddAccountAsync, () => !IsBusy);
        LaunchAccountCommand = new RelayCommand(p => LaunchAccountAsync(p as AccountSummary));
        RemoveAccountCommand = new RelayCommand(p => RemoveAccountAsync(p as AccountSummary));
        ReauthenticateCommand = new RelayCommand(p => ReauthenticateAsync(p as AccountSummary));
        OpenSettingsCommand = new RelayCommand(OpenSettings);
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
        catch
        {
            RobloxCompatBanner = null;
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
        catch
        {
            // Avatar fetch is best-effort — the row still works without an image.
        }

        var account = await _accountStore.AddAsync(captured.Username, avatarUrl, captured.Cookie);
        Accounts.Insert(0, new AccountSummary(account));
        StatusBanner = $"Added {captured.Username}.";
    }

    private async Task LaunchAccountAsync(AccountSummary? summary)
    {
        if (summary is null)
        {
            return;
        }

        summary.IsLaunching = true;
        summary.StatusText = "Launching...";
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

            // Per-account game pick from the row's ComboBox (in-memory, resets on app restart).
            // Launcher wraps bare placeId in PlaceLauncher.ashx form via NormalizeToPlaceLauncherUrl.
            // null SelectedGame falls through to launcher tier-2 (favorites default) then tier-3
            // (legacy AppSettings.DefaultPlaceUrl).
            var placeUrl = summary.SelectedGame?.PlaceId.ToString();
            var result = await _launcher.LaunchAsync(cookie, placeUrl);
            switch (result)
            {
                case LaunchResult.Started started:
                    await _accountStore.TouchLastLaunchedAsync(summary.Id);
                    summary.StampLaunched(DateTimeOffset.UtcNow);
                    summary.SessionExpired = false;
                    summary.StatusText = $"Launched (pid {started.Pid}).";
                    break;
                case LaunchResult.CookieExpired:
                    summary.SessionExpired = true;
                    summary.StatusText = "Session expired.";
                    break;
                case LaunchResult.Failed failed when failed.Message.Contains("Roblox does not appear to be installed", StringComparison.OrdinalIgnoreCase):
                    summary.StatusText = "Roblox not installed.";
                    ShowRobloxNotInstalledModal();
                    break;
                case LaunchResult.Failed failed:
                    summary.StatusText = failed.Message;
                    break;
            }
        }
        finally
        {
            summary.IsLaunching = false;
        }
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
            catch
            {
                // best-effort
            }

            await _accountStore.UpdateCookieAsync(summary.Id, success.Cookie);
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
