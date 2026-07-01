using System.Diagnostics;
using System.Windows;
using ROROROblox.App.Startup;
using ROROROblox.App.Theming;
using ROROROblox.App.Transport;
using ROROROblox.App.ViewModels;
using ROROROblox.Core;
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
    private bool _suppressClickHandlers; // true while we set the initial check states.

    public PreferencesWindow(
        IAppSettings settings,
        IStartupRegistration startupRegistration,
        IThemeStore themeStore,
        ThemeService themeService,
        IAccountStore accountStore,
        IAccountTransport transport,
        MainViewModel mainViewModel)
    {
        _settings = settings;
        _startupRegistration = startupRegistration;
        _themeStore = themeStore;
        _themeService = themeService;
        _accountStore = accountStore;
        _transport = transport;
        _mainViewModel = mainViewModel;
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
