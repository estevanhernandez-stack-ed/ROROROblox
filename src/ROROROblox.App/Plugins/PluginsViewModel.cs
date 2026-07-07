using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Distribution;
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
    private readonly ILogger<PluginsViewModel> _log;
    private readonly IDistributionMode _distributionMode;
    private readonly PluginCatalogClient _catalogClient;
    private readonly Version _hostVersion;

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
        Func<PluginManifest, Task<IReadOnlyList<string>?>> showConsentSheet,
        IDistributionMode distributionMode,
        PluginCatalogClient catalogClient,
        Version hostVersion,
        ILogger<PluginsViewModel>? log = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _registryAdapter = registryAdapter ?? throw new ArgumentNullException(nameof(registryAdapter));
        _consentStore = consentStore ?? throw new ArgumentNullException(nameof(consentStore));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _supervisor = supervisor ?? throw new ArgumentNullException(nameof(supervisor));
        _showConsentSheet = showConsentSheet ?? throw new ArgumentNullException(nameof(showConsentSheet));
        _distributionMode = distributionMode ?? throw new ArgumentNullException(nameof(distributionMode));
        _catalogClient = catalogClient ?? throw new ArgumentNullException(nameof(catalogClient));
        _hostVersion = hostVersion ?? throw new ArgumentNullException(nameof(hostVersion));
        _log = log ?? NullLogger<PluginsViewModel>.Instance;
        UpdatePluginCommand = new RelayCommand(p => UpdatePluginAsync(p as PluginRow), _ => !IsBusy);
        InstallFromCatalogCommand = new RelayCommand(p => InstallFromCatalogAsync(p as AvailablePluginRow), _ => !IsBusy);

        InstallFromUrlCommand = new RelayCommand(InstallAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(InstallUrlInput));
        ToggleAutostartCommand = new RelayCommand(p => ToggleAutostartAsync(p as PluginRow));
        RevokeCommand = new RelayCommand(p => RevokeAsync(p as PluginRow));
        RestartCommand = new RelayCommand(_ => RestartFromBannerAsync());
        LaunchPluginCommand = new RelayCommand(p => LaunchPluginAsync(p as PluginRow));

        // Subscribe to supervisor exit events (commit 3 wires the banner update). Detached
        // in Dispose so the window's Closed handler can unhook us cleanly.
        _supervisor.PluginExited += OnPluginExited;
    }

    public ObservableCollection<PluginRow> Plugins { get; } = new();

    /// <summary>Not-installed catalog plugins. Always empty in a packaged (Store/sideload) build.</summary>
    public ObservableCollection<AvailablePluginRow> Available { get; } = new();

    /// <summary>
    /// The marketplace (catalog fetch, Available section, update badges) is active ONLY when
    /// unpackaged. In a packaged MSIX build this is false and the window stays the paste-URL-only
    /// surface that policy 10.2.2 was certified against. See the design doc §2.
    /// </summary>
    public bool MarketplaceEnabled => !_distributionMode.IsPackaged;

    public ICommand UpdatePluginCommand { get; }
    public ICommand InstallFromCatalogCommand { get; }

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

    /// <summary>Per-row Launch — spawns the plugin EXE on demand without an install or
    /// RoRoRo restart. Belt-and-suspenders with install-time autostart for the case where
    /// autostart is intentionally off but the user wants to run the plugin now.</summary>
    public ICommand LaunchPluginCommand { get; }

    public async Task LoadAsync()
    {
        var installed = await _registry.ScanAsync().ConfigureAwait(true);
        var running = _supervisor.RunningPids;

        // Fetch the catalog ONLY when unpackaged — the packaged build must never read a curated list
        // from a server (policy 10.2.2). MarketplacePlan then joins installed + catalog + host version.
        IReadOnlyList<PluginCatalogEntry> catalog = [];
        if (MarketplaceEnabled)
        {
            using var catalogTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            catalog = await _catalogClient.FetchAsync(catalogTimeout.Token).ConfigureAwait(true);
        }
        var view = MarketplacePlan.Build(installed, catalog, _hostVersion);

        Plugins.Clear();
        foreach (var iv in view.Installed)
        {
            var row = new PluginRow(iv.Plugin, isRunning: running.ContainsKey(iv.Plugin.Manifest.Id));
            if (iv.Update is PluginUpdateState.UpdateAvailable upd)
            {
                row.SetUpdateAvailable($"Update available ({upd.FromVersion} → {upd.ToVersion})", iv.UpdateInstallUrl);
            }
            Plugins.Add(row);
        }

        Available.Clear();
        foreach (var av in view.Available)
        {
            Available.Add(new AvailablePluginRow(av));
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
                _log.LogInformation(
                    "Plugin consent: sheet cancelled for {PluginId} v{Version} during install — install dir rolled back.",
                    installed.Manifest.Id, installed.Manifest.Version);
                TryDeleteInstallDir(installed.InstallDir);
                StatusBanner = "Install cancelled.";
                return;
            }

            _log.LogInformation(
                "Plugin consent: granted {GrantedCount}/{DeclaredCount} capabilities to {PluginId} v{Version} at install: [{Granted}].",
                granted.Count, installed.Manifest.Capabilities.Count, installed.Manifest.Id,
                installed.Manifest.Version, string.Join(", ", granted));
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
                // LoadAsync above already rebuilt Plugins. Flip the matching row's
                // IsRunning so the Launch button on it shows disabled-state immediately.
                var newRow = Plugins.FirstOrDefault(p => p.Plugin.Manifest.Id == installed.Manifest.Id);
                if (newRow is not null) newRow.IsRunning = true;
                StatusBanner = $"{installed.Manifest.Name} installed and running.";
            }
            catch (Exception startEx)
            {
                _log.LogWarning(startEx, "Plugin {PluginId} installed but post-install start failed.", installed.Manifest.Id);
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
            // Log the exception type + a URL-REDACTED message. The installer builds some
            // messages by interpolating the pasted URL ("GET {uri} returned {status}"), and a
            // signed/SAS URL carries a secret in its query string — so scrub http(s) URLs before
            // logging. That keeps the actual diagnostic reason (SHA256 mismatch, minHostVersion,
            // HTTP status, extraction / access-denied, missing entrypoint) in the support log
            // while the secret stays out. Previously only the type was logged, which named the
            // failure class but never the cause — a failed install was effectively undiagnosable
            // from the log, which is exactly the hole a real user fell into.
            var safeReason = System.Text.RegularExpressions.Regex.Replace(
                ex.Message, @"https?://\S+", "<url>");
            _log.LogWarning("Plugin install failed: {ExceptionType}: {Reason}; install dir rolled back if present.",
                ex.GetType().Name, safeReason);
            StatusBanner = $"Install failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UpdatePluginAsync(PluginRow? row)
    {
        if (row?.UpdateInstallUrl is not { } url) return;
        var wasRunning = _supervisor.RunningPids.ContainsKey(row.Plugin.Manifest.Id);
        IsBusy = true;
        StatusBanner = null;
        try
        {
            // The installer stops any running instance out of the plugin's dir before re-extracting,
            // then unpacks the new version. Same SHA-verified path as a fresh install.
            var updated = await _installer.InstallAsync(url, Array.Empty<string>()).ConfigureAwait(true);
            _log.LogInformation("Plugin {PluginId} updated to v{Version}.", updated.Manifest.Id, updated.Manifest.Version);
            _registryAdapter.Refresh();
            await LoadAsync().ConfigureAwait(true);
            if (wasRunning)
            {
                _supervisor.Start(updated); // relaunch on the new version, only if it was running before
                var newRow = Plugins.FirstOrDefault(p => p.Plugin.Manifest.Id == updated.Manifest.Id);
                if (newRow is not null) newRow.IsRunning = true;
            }
            StatusBanner = $"{updated.Manifest.Name} updated to {updated.Manifest.Version}.";
        }
        catch (Exception ex)
        {
            _log.LogWarning("Plugin update failed (url input): {ExceptionType}.", ex.GetType().Name);
            StatusBanner = $"Update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallFromCatalogAsync(AvailablePluginRow? row)
    {
        if (row is null) return;
        // Reuse the exact URL-install path (consent sheet included) — the catalog just pre-fills the URL.
        InstallUrlInput = row.InstallUrl;
        await InstallAsync().ConfigureAwait(true);
    }

    internal async Task ToggleAutostartAsync(PluginRow? row)
    {
        if (row is null) return;
        try
        {
            var nextEnabled = !row.AutostartEnabled;
            await _consentStore.SetAutostartAsync(row.Plugin.Manifest.Id, nextEnabled).ConfigureAwait(true);
            _registryAdapter.Refresh();
            await LoadAsync().ConfigureAwait(true);
            _log.LogInformation("Plugin {PluginId}: autostart {State}.", row.Plugin.Manifest.Id, nextEnabled ? "enabled" : "disabled");
            StatusBanner = nextEnabled
                ? $"Autostart enabled for {row.Name}."
                : $"Autostart disabled for {row.Name}.";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Autostart toggle failed: {ex.Message}";
        }
    }

    internal async Task LaunchPluginAsync(PluginRow? row)
    {
        if (row is null) return;
        if (row.IsRunning) return;                    // belt-and-suspenders — XAML disables the
                                                      // button on IsRunning, but a stale binding
                                                      // shouldn't ever hand us a double-launch.
        try
        {
            // A plugin with no consent record never met the consent sheet — the sheet
            // otherwise only runs inside the install-from-URL flow, so a dev-dropped
            // plugin (files copied into the plugins root by hand) would launch with
            // zero grants and every gated call would bounce. Show the sheet here on
            // first Launch instead. An EXISTING record — even one with zero grants —
            // is a deliberate user decision and never re-prompts.
            var pluginId = row.Plugin.Manifest.Id;
            var hasRecord = (await _consentStore.ListAsync().ConfigureAwait(true))
                .Any(r => r.PluginId == pluginId);
            if (!hasRecord)
            {
                var granted = await _showConsentSheet(row.Plugin.Manifest).ConfigureAwait(true);
                if (granted is null)
                {
                    _log.LogInformation(
                        "Plugin consent: sheet cancelled for {PluginId} on first Launch — not started, nothing persisted.",
                        pluginId);
                    StatusBanner = $"{row.Name} not started — consent sheet cancelled.";
                    return;
                }
                _log.LogInformation(
                    "Plugin consent: granted {GrantedCount}/{DeclaredCount} capabilities to {PluginId} on first Launch: [{Granted}].",
                    granted.Count, row.Plugin.Manifest.Capabilities.Count, pluginId, string.Join(", ", granted));
                await _consentStore.GrantAsync(pluginId, granted).ConfigureAwait(true);
                _registryAdapter.Refresh();
                await LoadAsync().ConfigureAwait(true);
                // LoadAsync rebuilt Plugins — re-find the row so IsRunning lands on the
                // instance the XAML list is actually bound to now.
                row = Plugins.FirstOrDefault(p => p.Plugin.Manifest.Id == pluginId) ?? row;
            }

            StatusBanner = $"Launching {row.Name}...";
            _supervisor.Start(row.Plugin);
            row.IsRunning = true;
            StatusBanner = $"{row.Name} is running.";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Plugin {PluginId} launch-from-row failed.", row.Plugin.Manifest.Id);
            StatusBanner = $"Launch failed: {ex.Message}";
        }
    }

    internal async Task RevokeAsync(PluginRow? row)
    {
        if (row is null) return;
        try
        {
            // Stop the plugin FIRST so its DLL handles release before we delete the install
            // dir — a running plugin holds its own EXE + dependent DLLs open, and without this
            // TryDeleteInstallDir silently fails, leaving the plugin "removed" but still on
            // disk + in memory. StopByInstallDirAsync also catches orphans (a process that
            // outlived the RoRoRo session that started it) — plain Stop only kills what this
            // session's PID map tracks, so an orphan would slip through.
            await _supervisor.StopByInstallDirAsync(row.Plugin.Manifest.Id, row.Plugin.InstallDir)
                .ConfigureAwait(true);

            await _consentStore.RevokeAsync(row.Plugin.Manifest.Id).ConfigureAwait(true);
            TryDeleteInstallDir(row.Plugin.InstallDir);
            _registryAdapter.Refresh();
            await LoadAsync().ConfigureAwait(true);
            _log.LogInformation("Plugin consent: revoked + removed {PluginId} (consent record deleted, install dir cleaned).", row.Plugin.Manifest.Id);
            StatusBanner = $"{row.Name} removed.";
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Plugin {PluginId} remove failed.", row.Plugin.Manifest.Id);
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
            _log.LogInformation("Plugin {PluginId}: restarted from exit banner.", match.Plugin.Manifest.Id);
            StatusBanner = $"{match.Name} restarted.";
            _bannerPluginId = null;
            Raise(nameof(BannerIsRestartable));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Plugin {PluginId} restart-from-banner failed.", _bannerPluginId);
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
            // Flip the row's IsRunning so the Launch button comes back enabled. Safe when
            // match is null (uninstalled mid-exit) — null-skip the row state mutation.
            if (match is not null) match.IsRunning = false;
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
/// names. Most mutations route through the parent VM's commands; the parent rebuilds the
/// observable list after each mutation, so most fields don't need INPC. <see cref="IsRunning"/>
/// is the exception — it flips on every Start / process-exit pair WITHOUT a list rebuild,
/// so the row implements INPC to drive the Launch button's IsEnabled binding live.
/// </summary>
internal sealed class PluginRow : INotifyPropertyChanged
{
    private bool _isRunning;

    public PluginRow(InstalledPlugin p, bool isRunning = false)
    {
        Plugin = p ?? throw new ArgumentNullException(nameof(p));
        _isRunning = isRunning;
    }

    public InstalledPlugin Plugin { get; }

    public string Name => Plugin.Manifest.Name;
    public string Version => Plugin.Manifest.Version;
    public string Publisher => Plugin.Manifest.Publisher;
    public string Description => Plugin.Manifest.Description;
    public int CapabilityCount => Plugin.Manifest.Capabilities.Count;
    public bool AutostartEnabled => Plugin.Consent.AutostartEnabled;

    /// <summary>True when the supervisor reports an active process for this plugin id.
    /// Bound by XAML to invert into the Launch button's IsEnabled.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        internal set
        {
            if (_isRunning == value) return;
            _isRunning = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRunning)));
        }
    }

    /// <summary>"3 capabilities" / "1 capability" — the row chip label.</summary>
    public string CapabilitySummary => CapabilityCount == 1
        ? "1 capability"
        : $"{CapabilityCount} capabilities";

    private bool _updateAvailable;
    /// <summary>True when the catalog lists a newer version than the installed one. Drives the
    /// update badge + Update button in the window.</summary>
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set { if (_updateAvailable != value) { _updateAvailable = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateAvailable))); } }
    }

    /// <summary>"Update available (0.1.0 → 0.2.0)" — the badge text. Empty when up to date.</summary>
    public string UpdateLabel { get; private set; } = string.Empty;

    /// <summary>The catalog installUrl to update from. Null when no update is available.</summary>
    public string? UpdateInstallUrl { get; private set; }

    internal void SetUpdateAvailable(string label, string? installUrl)
    {
        UpdateLabel = label;
        UpdateInstallUrl = installUrl;
        UpdateAvailable = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// A catalog plugin the user does NOT have installed — the Available section's row. Wraps
/// <see cref="AvailablePluginView"/> so XAML binds friendly names.
/// </summary>
internal sealed class AvailablePluginRow
{
    private readonly AvailablePluginView _view;

    public AvailablePluginRow(AvailablePluginView view) => _view = view ?? throw new ArgumentNullException(nameof(view));

    public string Id => _view.Entry.Id;
    public string Name => _view.Entry.Name;
    public string Publisher => _view.Entry.Publisher;
    public string Description => _view.Entry.Description;
    public string Version => _view.Entry.LatestVersion;
    public string InstallUrl => _view.Entry.InstallUrl;
    public bool Installable => _view.Installable;

    /// <summary>"Install" when installable, else the reason it isn't.</summary>
    public string ActionLabel => Installable ? "Install" : $"Needs RoRoRo {_view.Entry.MinHostVersion}+";
}
