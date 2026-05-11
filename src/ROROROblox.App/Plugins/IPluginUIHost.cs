namespace ROROROblox.App.Plugins;

/// <summary>
/// The WPF-side host that <see cref="PluginUITranslator"/> dispatches into. v1 maps
/// plugin UI primitives (tray menu items, row badges, status panels) onto the App's
/// real UI surfaces; tests substitute a fake to exercise the translator without WPF.
///
/// Each Add* call returns an opaque handle id (string). RemoveUI / Update look up
/// the handle to mutate the matching UI element. The translator owns ownership
/// tracking — a plugin can only remove handles it created.
///
/// Item 15 (DI wiring) wires the production implementation against MainWindow's
/// tray, account-row collection, and status-panel pane.
/// </summary>
public interface IPluginUIHost
{
    string AddTrayMenuItem(string pluginId, string label, string? tooltip, bool enabled, Action onClick);
    string AddRowBadge(string pluginId, string text, string? colorHex, string? tooltip);
    string AddStatusPanel(string pluginId, string title, string bodyMarkdown);
    void Update(string handle, string newLabel);
    void Remove(string handle);
}
