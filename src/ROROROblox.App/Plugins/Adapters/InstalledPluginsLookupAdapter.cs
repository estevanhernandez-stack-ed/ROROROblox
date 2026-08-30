using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Sync adapter over the async <see cref="PluginRegistry.ScanAsync"/>. The handshake +
/// capability paths need a synchronous <see cref="IInstalledPluginsLookup.FindById"/>;
/// PluginRegistry is async because manifest parsing + consent decryption both touch
/// disk. Bridge: scan once at construction, cache the snapshot, expose a manual
/// <see cref="Refresh"/> hook for anything that mutates the registry. The construction-time
/// snapshot covers handshake + capability lookup on its own; <c>PluginsViewModel</c> calls
/// Refresh after install, update, uninstall, an autostart toggle, and a first-launch consent
/// grant, so the index tracks the Plugins page without a restart. (This comment used to say
/// that wiring was still to come in v1.4 item 16; it has been in place since then.)
/// </summary>
public sealed class InstalledPluginsLookupAdapter : IInstalledPluginsLookup
{
    private readonly PluginRegistry _registry;
    private readonly ILogger<InstalledPluginsLookupAdapter> _log;
    private readonly object _lock = new();
    private Dictionary<string, InstalledPlugin> _byId = new(StringComparer.Ordinal);

    public InstalledPluginsLookupAdapter(
        PluginRegistry registry,
        ILogger<InstalledPluginsLookupAdapter>? log = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _log = log ?? NullLogger<InstalledPluginsLookupAdapter>.Instance;
        // Initial snapshot — block on the async scan since the DI factory is sync.
        // ScanAsync is small (a few file reads + DPAPI unprotect), and this runs
        // exactly once at App startup before MainWindow.Show.
        Refresh();
    }

    public InstalledPlugin? FindById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock)
        {
            return _byId.TryGetValue(id, out var plugin) ? plugin : null;
        }
    }

    /// <summary>
    /// Re-scan the plugins root and rebuild the in-memory index. <c>PluginsViewModel</c>
    /// (the Plugins page) calls this after install, update, uninstall, an autostart toggle,
    /// and a first-launch consent grant.
    /// </summary>
    public void Refresh()
    {
        var fresh = new Dictionary<string, InstalledPlugin>(StringComparer.Ordinal);
        try
        {
            var plugins = _registry.ScanAsync().GetAwaiter().GetResult();

            // Index by id, keeping the FIRST folder that claims each one.
            //
            // This used to be plugins.ToDictionary(p => p.Manifest.Id), one statement BELOW the
            // catch whose comment promises a corrupt plugins root cannot stop the App from
            // launching — so the promise was defeated by the next line. Two folders declaring the
            // same manifest id threw ArgumentException, the constructor threw, resolving
            // PluginHostStartupService threw, and App logged "plugins disabled this session" at
            // DEBUG. Every plugin gone, and no message anywhere a user would look.
            //
            // The trigger is not exotic. Copying a plugin folder to back it up before an update
            // does it, which is exactly how this was found. A duplicate id is a reason to ignore
            // one folder, never a reason to disable plugin support entirely.
            //
            // The keep-the-first rule moved to PluginDuplicates (F-099) because the Plugins window
            // needs the same answer and had been reaching a different one — it deduplicated
            // nowhere, so it drew the plugin twice.
            var (kept, dropped) = PluginDuplicates.Resolve(plugins);
            foreach (var plugin in kept)
            {
                fresh[plugin.Manifest.Id] = plugin;
            }

            foreach (var d in dropped)
            {
                // Warning, not Debug: two folders claim one plugin and one is being ignored.
                // Both paths are named so the offending copy is findable. The Plugins window now
                // says the same thing where the user can actually see it.
                _log.LogWarning(
                    "Two installed folders declare plugin id '{Id}'. Using '{Kept}' and ignoring "
                    + "'{Ignored}'. Delete or move the unused copy — a duplicate id is usually a "
                    + "leftover backup folder.",
                    d.Id, d.KeptDir, d.IgnoredDir);
            }
        }
        catch (Exception ex)
        {
            // Defensive: a corrupt plugins root must not stop the App from launching.
            // Fall back to an empty index — handshakes will reject all plugin ids.
            _log.LogWarning(ex, "Scanning the plugins root failed; continuing with no plugins.");
            fresh.Clear();
        }

        lock (_lock)
        {
            _byId = fresh;
        }
    }
}
