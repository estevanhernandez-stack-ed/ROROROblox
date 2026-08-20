namespace ROROROblox.App.Plugins;

/// <summary>
/// Decides which folder wins when two of them declare the same plugin id, and reports the ones that
/// lost.
/// <para>
/// WHY THIS IS SHARED (F-099). Two paths read the plugins root: the host's
/// <c>InstalledPluginsLookupAdapter</c>, which indexes by id and keeps the first folder, and the
/// Plugins window, which listed whatever <c>ScanAsync</c> returned. Only the host deduplicated. The
/// window therefore rendered the plugin TWICE — two identical rows, each with Launch, Revoke and
/// Restart, and no way to tell which one the host was actually running. Acting on the ignored row
/// operated on a folder the host had already discarded.
/// </para>
/// <para>
/// One rule, in one place, so the window and the host can never disagree about which copy is live.
/// The register's own note about this repo shipping a fix into one scanner and missing its copy in
/// another is the reason this is not two <c>foreach</c> loops.
/// </para>
/// </summary>
internal static class PluginDuplicates
{
    /// <param name="Id">The manifest id both folders declare.</param>
    /// <param name="KeptDir">The folder that wins — the one the host loads.</param>
    /// <param name="IgnoredDir">The folder that is being ignored.</param>
    internal readonly record struct Dropped(string Id, string KeptDir, string IgnoredDir);

    /// <summary>
    /// Splits <paramref name="plugins"/> into the copies that win and the copies that are ignored.
    /// First folder wins, matching the host's long-standing behaviour — a duplicate id is a reason
    /// to ignore one folder, never a reason to disable plugin support.
    /// </summary>
    internal static (IReadOnlyList<InstalledPlugin> Kept, IReadOnlyList<Dropped> Dropped) Resolve(
        IReadOnlyList<InstalledPlugin>? plugins)
    {
        if (plugins is null || plugins.Count == 0) return ([], []);

        var keptById = new Dictionary<string, InstalledPlugin>(StringComparer.Ordinal);
        var kept = new List<InstalledPlugin>(plugins.Count);
        var dropped = new List<Dropped>();

        foreach (var plugin in plugins)
        {
            var id = plugin.Manifest?.Id;
            // An id-less manifest cannot collide with anything and cannot be indexed. The host skips
            // these; keep that, so the two paths still agree on the population.
            if (string.IsNullOrEmpty(id)) continue;

            if (keptById.TryGetValue(id, out var winner))
            {
                dropped.Add(new Dropped(id, winner.InstallDir, plugin.InstallDir));
                continue;
            }

            keptById[id] = plugin;
            kept.Add(plugin);
        }

        return (kept, dropped);
    }

    /// <summary>
    /// The sentence the Plugins window shows. Names the id, both folders and which one is live,
    /// because the recovery instruction — delete the unused copy — is useless without the path.
    /// </summary>
    /// <remarks>
    /// Deliberately not a modal: a plugin the user still has is not an interruption. This is a
    /// standing description of the plugins root, so it lives in a banner that survives until the
    /// next scan rather than in the transient install-outcome line.
    /// </remarks>
    internal static string Describe(IReadOnlyList<Dropped> dropped)
    {
        if (dropped is null || dropped.Count == 0) return "";

        if (dropped.Count == 1)
        {
            var d = dropped[0];
            return $"Two folders declare the plugin \"{d.Id}\". RoRoRo is running the copy in "
                 + $"{d.KeptDir} and ignoring {d.IgnoredDir}. Delete the unused copy — it is usually "
                 + "a leftover backup folder.";
        }

        var ids = string.Join(", ", dropped.Select(d => $"\"{d.Id}\"").Distinct(StringComparer.Ordinal));
        var paths = string.Join("; ", dropped.Select(d => d.IgnoredDir));
        return $"{dropped.Count} plugin folders are being ignored because another folder already "
             + $"declares the same id ({ids}). Ignored: {paths}. Delete the unused copies — they are "
             + "usually leftover backup folders.";
    }
}
