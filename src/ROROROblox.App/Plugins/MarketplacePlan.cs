namespace ROROROblox.App.Plugins;

/// <summary>An installed plugin's update status relative to the catalog.</summary>
internal abstract record PluginUpdateState
{
    private PluginUpdateState() { }

    public sealed record UpToDate : PluginUpdateState;
    public sealed record UpdateAvailable(string FromVersion, string ToVersion) : PluginUpdateState;
}

internal sealed record InstalledPluginView(InstalledPlugin Plugin, PluginUpdateState Update, string? UpdateInstallUrl);

internal sealed record AvailablePluginView(PluginCatalogEntry Entry, bool Installable);

internal sealed record MarketplaceView(
    IReadOnlyList<InstalledPluginView> Installed,
    IReadOnlyList<AvailablePluginView> Available);

/// <summary>
/// Pure join of installed plugins + catalog + host version into the marketplace's view model:
/// per-installed update status (matched by id; "update available" only when the catalog's
/// latestVersion parses strictly newer than the installed version), and the not-installed catalog
/// entries as Available (each flagged installable unless a parseable minHostVersion exceeds the host).
/// No I/O — the one place marketplace version math lives.
/// </summary>
internal static class MarketplacePlan
{
    public static MarketplaceView Build(
        IReadOnlyList<InstalledPlugin> installed,
        IReadOnlyList<PluginCatalogEntry> catalog,
        Version hostVersion)
    {
        var catalogById = new Dictionary<string, PluginCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog)
        {
            catalogById[entry.Id] = entry; // last wins on a dup id — catalog-authoring bug, not ours to fix here
        }

        var installedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var installedViews = new List<InstalledPluginView>(installed.Count);
        foreach (var plugin in installed)
        {
            installedIds.Add(plugin.Manifest.Id);

            PluginUpdateState state = new PluginUpdateState.UpToDate();
            string? updateUrl = null;
            if (catalogById.TryGetValue(plugin.Manifest.Id, out var entry)
                && TryParseVersion(plugin.Manifest.Version, out var current)
                && TryParseVersion(entry.LatestVersion, out var latest)
                && latest > current)
            {
                state = new PluginUpdateState.UpdateAvailable(plugin.Manifest.Version, entry.LatestVersion);
                updateUrl = entry.InstallUrl;
            }
            installedViews.Add(new InstalledPluginView(plugin, state, updateUrl));
        }

        var availableViews = new List<AvailablePluginView>();
        foreach (var entry in catalog)
        {
            if (installedIds.Contains(entry.Id))
            {
                continue;
            }
            availableViews.Add(new AvailablePluginView(entry, Installable(entry, hostVersion)));
        }

        return new MarketplaceView(installedViews, availableViews);
    }

    // Installable unless a PARSEABLE minHostVersion is newer than the host. An unparseable
    // minHostVersion fails open here — the install-time check in PluginInstaller is the real gate.
    private static bool Installable(PluginCatalogEntry entry, Version hostVersion)
    {
        if (string.IsNullOrWhiteSpace(entry.MinHostVersion))
        {
            return true;
        }
        return !TryParseVersion(entry.MinHostVersion, out var min) || hostVersion >= min;
    }

    // Lenient parse mirroring PluginInstaller.TryParseHostVersion: split on the first '-' and parse
    // the numeric head, so "1.4.3-beta" is treated as "1.4.3".
    private static bool TryParseVersion(string input, out Version version)
    {
        var head = input;
        var dash = head.IndexOf('-');
        if (dash >= 0) head = head[..dash];
        return Version.TryParse(head, out version!);
    }
}
