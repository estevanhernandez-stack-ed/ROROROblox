using System.IO;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Disk-scanned + in-memory list of installed plugins. Default plugins root:
/// <c>%LOCALAPPDATA%\ROROROblox\plugins\</c>. Pairs each on-disk manifest with
/// the user's consent record (or an empty default).
/// </summary>
public sealed class PluginRegistry
{
    public static string DefaultPluginsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox", "plugins");

    private readonly string _pluginsRoot;
    private readonly ConsentStore _consentStore;

    public PluginRegistry(string pluginsRoot, ConsentStore consentStore)
    {
        _pluginsRoot = pluginsRoot ?? throw new ArgumentNullException(nameof(pluginsRoot));
        _consentStore = consentStore ?? throw new ArgumentNullException(nameof(consentStore));
    }

    public async Task<IReadOnlyList<InstalledPlugin>> ScanAsync()
    {
        if (!Directory.Exists(_pluginsRoot))
        {
            return Array.Empty<InstalledPlugin>();
        }

        var consentByPluginId = (await _consentStore.ListAsync().ConfigureAwait(false))
            .ToDictionary(r => r.PluginId, StringComparer.Ordinal);

        var plugins = new List<InstalledPlugin>();
        foreach (var dir in Directory.EnumerateDirectories(_pluginsRoot))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false);
                var manifest = PluginManifest.Parse(json);
                var consent = consentByPluginId.TryGetValue(manifest.Id, out var existing)
                    ? existing
                    : new ConsentRecord
                    {
                        PluginId = manifest.Id,
                        GrantedCapabilities = Array.Empty<string>(),
                        AutostartEnabled = false,
                    };
                plugins.Add(new InstalledPlugin
                {
                    Manifest = manifest,
                    InstallDir = dir,
                    Consent = consent,
                });
            }
            catch (PluginManifestException)
            {
                // Skip malformed manifests; surface in logs at the caller.
                continue;
            }
        }
        return plugins;
    }
}
