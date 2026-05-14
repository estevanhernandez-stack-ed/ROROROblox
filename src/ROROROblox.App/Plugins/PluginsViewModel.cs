using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ROROROblox.App.Plugins.Adapters;
using ROROROblox.App.ViewModels;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Backs <see cref="PluginsWindow"/>. Owns the observable list of installed plugins and
/// the install / autostart-toggle / revoke / restart commands. Keeps the UI thread off
/// disk + DPAPI by funnelling every mutation through async methods that reload from
/// <see cref="PluginRegistry.ScanAsync"/> when they're done.
///
/// The consent sheet itself is a separate window (commit 2). The VM accepts a callback
/// so the install flow can hand off a manifest and wait for a granted-capabilities list
/// (or null on cancel) without depending on the WPF window class directly — keeps the VM
/// testable and the install flow re-usable from the future "install from clipboard URL"
/// shortcut.
/// </summary>
internal sealed class PluginsViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly PluginRegistry _registry;
    private readonly InstalledPluginsLookupAdapter _registryAdapter;
    private readonly ConsentStore _consentStore;
    private readonly PluginInstaller _installer;
    private readonly PluginProcessSupervisor _supervisor;
    private readonly Func<PluginManifest, Task<IReadOnlyList<string>?>> _showConsentSheet;

    private string _installUrlInput = string.Empty;
    private string? _statusBanner;
    private string? _bannerPluginId; // tracks which plugin the banner refers to (commit 3)
    private bool _isBusy;
    private bool _disposed;

    public PluginsViewModel(
        PluginRegistry registry,
        InstalledPluginsLookupAdapter registryAdapter,
        ConsentStore consentStore,
        PluginInstaller installer,
        PluginProcessSupervisor supervisor,
        Func<PluginManifest, Task<IReadOnlyList<string>?>> showConsentSheet)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _registryAdapter = registryAdapter ?? throw new ArgumentNullException(nameof(registryAdapter));
        _consentStore = consentStore ?? throw new ArgumentNullException(nameof(consentStore));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _showConsentSheet = showConsentSheet ?? throw new ArgumentNullException(nameof(showConsentSheet));

        InstallFromUrlCommand = new RelayCommand(InstallAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(InstallUrlInput));
        ToggleAutostartCommand = new RelayCommand(p => ToggleAutostartAsync(p as PluginRow));
        RevokeCommand = new RelayCommand(p => RevokeAsync(p as PluginRow));
        RestartCommand = new RelayCommand(_ => RestartFromBannerAsync());

        // Subscribe to supervisor exit events (commit 3 wires the banner update). Detached
        // in Dispose so the window's Closed handler can unhook us cleanly.
        _supervisor.PluginExited += OnPluginExited;
    }

    public ObservableCollection<PluginRow> Plugins { get; } = new();

    public string InstallUrlInput
    {
        get => _installUrlInput;
        set { if (_installUrlInput != value) { _installUrlInput = value; Raise(); RelayCommand.RaiseCanExecuteChanged(); } }
    }

    public string? StatusBanner
    {
        get => _statusBanner;
        set { if (_statusBanner != value) { _statusBanner = value; Raise(); } }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { if (_isBusy != value) { _isBusy = value; Raise(); RelayCommand.RaiseCanExecuteChanged(); } }
    }

    /// <summary>
    /// True when the banner is referencing a plugin that exited and offers a Restart action.
    /// XAML uses this to swap the banner cosmetic from "info" (cyan strip) to "actionable"
    /// (clickable + magenta accent).
    /// </summary>
    public bool BannerIsRestartable
    {
        get => _bannerPluginId is not null;
    }

    public ICommand InstallFromUrlCommand { get; }
    public ICommand ToggleAutostartCommand { get; }
    public ICommand RevokeCommand { get; }
    public ICommand RestartCommand { get; }

    public async Task LoadAsync()
    {
        var installed = await _registry.ScanAsync().ConfigureAwait(true);
        Plugins.Clear();
        foreach (var p in installed)
        {
            Plugins.Add(new PluginRow(p));
        }
    }

    private async Task InstallAsync()
    {
        if (string.IsNullOrWhiteSpace(InstallUrlInput)) return;
        IsBusy = true;
        StatusBanner = null;
        InstalledPlugin? installed = null;
        try
        {
            installed = await _installer.InstallAsync(InstallUrlInput.Trim(), Array.Empty<string>())
                .ConfigureAwait(true);

            // Hand the manifest to the consent sheet. Null = user cancelled — roll back the
            // install dir so a half-installed plugin doesn't haunt the next scan.
            var granted = await _showConsentSheet(installed.Manifest).ConfigureAwait(true);
            if (granted is null)
            {
                TryDeleteInstallDir(installed.InstallDir);
                StatusBanner = "Install cancelled.";
                return;
            }

            await _consentStore.GrantAsync(installed.Manifest.Id, granted).ConfigureAwait(true);
            _registryAdapter.Refresh();
            await LoadAsync().ConfigureAwait(true);

            // Start the plugin process now — no RoRoRo restart needed. The per-row
            // Autostart toggle only governs whether it ALSO launches on future RoRoRo
            // starts. A start failure here is non-fatal: the plugin is installed, and
            // the user can still toggle autostart and restart RoRoRo. InstalledPlugin
            // .ExecutablePath is a computed property (InstallDir + id + ".exe"), so the
            // installer's return value is sufficient — no rescan needed.
            try
            {
                _supervisor.Start(installed);
                StatusBanner = $"{installed.Manifest.Name} installed and running.";
            }
            catch (Exception startEx)
            {
                StatusBanner = $"{installed.Manifest.Name} installed — start failed: {startEx.Message}";
            }

            InstallUrlInput = string.Empty;
        }
        catch (Exception ex)
        {
            // Best-effort cleanup: if we got past unpack but exploded after, the dir is on disk
            // and nothing in the consent store points to it — clean it so re-install works.
            if (installed is not null)
            {
                TryDeleteInstallDir(installed.InstallDir);
            }
            StatusBanner = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleAutostartAsync(PluginRow? row)
    {
        if (row is null) return;
        try
        {
            var nextEnabled = !row.AutostartEnabled;
            await _consentStore.SetAutostartAsync(row.Plugin.Manifest.Id, nextEnabled).ConfigureAwait(true);
            _registryAdapter.Refresh();
            await LoadAsync().ConfigureAwait(true);
            StatusBanner = nextEnabled
                ? $"Autostart enabled for {row.Name}."
                : $"Autostart disabled for {row.Name}.";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Autostart toggle failed: {ex.Message}";
        }
    }

    private async Task RevokeAsync(PluginRow? row)
    {
        if (row is null) return;
        try
        {
            // Kill the running plugin process FIRST so its DLL handles release before we
            // try to delete the install dir. Plugin processes hold their own EXE + dependent
            // DLLs open while running — without this, TryDeleteInstallDir silently fails and
            // the user is left with a "removed" plugin that's still in memory + on disk.
            // Brief delay lets the OS finish releasing handles before the delete attempt.
            _supervisor.Stop(row.Plugin.Manifest.Id);
            await Task.Delay(150).ConfigureAwait(true);

            await _consentStore.RevokeAsync(row.Plugin.Manifest.Id).ConfigureAwait(true);
            TryDeleteInstallDir(row.Plugin.InstallDir);
            _registryAdapter.Refresh();
            await LoadAsync().ConfigureAwait(true);
            StatusBanner = $"{row.Name} removed.";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Remove failed: {ex.Message}";
        }
    }


    private Task RestartFromBannerAsync()
    {
        if (_bannerPluginId is null) return Task.CompletedTask;
        var match = Plugins.FirstOrDefault(p => p.Plugin.Manifest.Id == _bannerPluginId);
        if (match is null)
        {
            // The plugin was uninstalled between exit + click; clear banner gracefully.
            StatusBanner = null;
            _bannerPluginId = null;
            Raise(nameof(BannerIsRestartable));
            return Task.CompletedTask;
        }
        try
        {
            _supervisor.Restart(match.Plugin);
            StatusBanner = $"{match.Name} restarted.";
            _bannerPluginId = null;
            Raise(nameof(BannerIsRestartable));
        }
        catch (Exception ex)
        {
            StatusBanner = $"Restart failed: {ex.Message}";
        }
        return Task.CompletedTask;
    }

    private void OnPluginExited(string pluginId, int pid)
    {
        _ = pid;
        // Supervisor fires from a Process.Exited handler, which lands on a worker thread.
        // Marshal to the WPF dispatcher before mutating observable state — otherwise INPC
        // raises on a non-UI thread and the binding system throws.
        var dispatcher = Application.Current?.Dispatcher;
        Action update = () =>
        {
            var match = Plugins.FirstOrDefault(p => p.Plugin.Manifest.Id == pluginId);
            var displayName = match?.Name ?? pluginId;
            _bannerPluginId = pluginId;
            StatusBanner = $"{displayName} stopped -- click to restart.";
            Raise(nameof(BannerIsRestartable));
        };
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            update();
        }
        else
        {
            dispatcher.BeginInvoke(DispatcherPriority.Normal, update);
        }
    }

    private static void TryDeleteInstallDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort. A locked file (plugin process still running) leaves the dir until
            // next reboot — Windows reuse handles it.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _supervisor.PluginExited -= OnPluginExited;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Raise([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// Per-row VM. Wraps <see cref="InstalledPlugin"/> so XAML can bind to friendly property
/// names. Mutations route through the parent VM's commands; the parent rebuilds the
/// observable list after each mutation, so per-row INPC isn't needed for v1.4.
/// </summary>
internal sealed class PluginRow
{
    public PluginRow(InstalledPlugin p)
    {
        Plugin = p ?? throw new ArgumentNullException(nameof(p));
    }

    public InstalledPlugin Plugin { get; }

    public string Name => Plugin.Manifest.Name;
    public string Version => Plugin.Manifest.Version;
    public string Publisher => Plugin.Manifest.Publisher;
    public string Description => Plugin.Manifest.Description;
    public int CapabilityCount => Plugin.Manifest.Capabilities.Count;
    public bool AutostartEnabled => Plugin.Consent.AutostartEnabled;

    /// <summary>"3 capabilities" / "1 capability" — the row chip label.</summary>
    public string CapabilitySummary => CapabilityCount == 1
        ? "1 capability"
        : $"{CapabilityCount} capabilities";
}
