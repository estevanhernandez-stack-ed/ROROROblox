namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// Sync adapter over the async <see cref="PluginRegistry.ScanAsync"/>. The handshake +
/// capability paths need a synchronous <see cref="IInstalledPluginsLookup.FindById"/>;
/// PluginRegistry is async because manifest parsing + consent decryption both touch
/// disk. Bridge: scan once at construction, cache the snapshot, expose a manual
/// <see cref="Refresh"/> hook for items that mutate the registry (install, uninstall).
/// v1.4 wires Refresh from the Plugins page in item 16; for now construction-time
/// snapshot is enough for handshake + capability lookup.
/// </summary>
public sealed class InstalledPluginsLookupAdapter : IInstalledPluginsLookup
{
    private readonly PluginRegistry _registry;
    private readonly object _lock = new();
    private Dictionary<string, InstalledPlugin> _byId = new(StringComparer.Ordinal);

    public InstalledPluginsLookupAdapter(PluginRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
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
    /// Re-scan the plugins root and rebuild the in-memory index. Item 16 (Plugins
    /// page) calls this after install / uninstall / consent changes.
    /// </summary>
    public void Refresh()
    {
        IReadOnlyList<InstalledPlugin> plugins;
        try
        {
            plugins = _registry.ScanAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Defensive: a corrupt plugins root must not stop the App from launching.
            // Fall back to an empty index — handshakes will reject all plugin ids.
            plugins = Array.Empty<InstalledPlugin>();
        }
        var fresh = plugins.ToDictionary(p => p.Manifest.Id, StringComparer.Ordinal);
        lock (_lock)
        {
            _byId = fresh;
        }
    }
}
