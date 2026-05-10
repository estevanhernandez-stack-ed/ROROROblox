namespace ROROROblox.App.Plugins;

/// <summary>
/// Read-side abstraction over the set of installed plugins. The host service uses this
/// to validate handshakes (does this plugin id exist?) and to resolve consent state for
/// capability-gated RPCs in items 10-14.
/// </summary>
public interface IInstalledPluginsLookup
{
    /// <summary>
    /// Returns the installed plugin with the given reverse-DNS id, or null if no plugin
    /// with that id is installed.
    /// </summary>
    InstalledPlugin? FindById(string id);
}
