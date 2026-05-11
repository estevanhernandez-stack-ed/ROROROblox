using Microsoft.Extensions.Logging;

namespace ROROROblox.App.Plugins.Adapters;

/// <summary>
/// v1.4 stub for <see cref="IPluginUIHost"/>. Logs every Add/Remove/Update call but does
/// NOT actually wire to <see cref="Tray.ITrayService"/> or MainWindow surfaces yet — that
/// landing surface is item 16 (Plugins page UI + tray menu wiring + row-badge plumbing).
///
/// Behavior:
/// <list type="bullet">
///   <item>Add* returns a fresh GUID-string handle.</item>
///   <item>Remove silently no-ops on unknown handles (the gRPC interceptor + translator
///         already enforce ownership; defensive at this layer).</item>
///   <item>Update logs and stamps the new label on the cached spec — read-back is for
///         tests; the user-visible surface lands in item 16.</item>
/// </list>
/// Tracked Dictionary keeps Remove path-testable and provides a hook for item 16's
/// real surface — the replacement implementation can mirror the same handle ids.
/// </summary>
public sealed class WpfPluginUIHost : IPluginUIHost
{
    private readonly ILogger<WpfPluginUIHost> _log;
    private readonly Dictionary<string, StubElement> _elements = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public WpfPluginUIHost(ILogger<WpfPluginUIHost> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public string AddTrayMenuItem(string pluginId, string label, string? tooltip, bool enabled, Action onClick)
    {
        var handle = NewHandle();
        var element = new StubElement(pluginId, "TrayMenuItem", label, onClick);
        lock (_lock) { _elements[handle] = element; }
        _log.LogInformation("WpfPluginUIHost STUB: AddTrayMenuItem plugin={Plugin} handle={Handle} label={Label} tooltip={Tooltip} enabled={Enabled}",
            pluginId, handle, label, tooltip, enabled);
        return handle;
    }

    public string AddRowBadge(string pluginId, string text, string? colorHex, string? tooltip)
    {
        var handle = NewHandle();
        var element = new StubElement(pluginId, "RowBadge", text, null);
        lock (_lock) { _elements[handle] = element; }
        _log.LogInformation("WpfPluginUIHost STUB: AddRowBadge plugin={Plugin} handle={Handle} text={Text} colorHex={ColorHex} tooltip={Tooltip}",
            pluginId, handle, text, colorHex, tooltip);
        return handle;
    }

    public string AddStatusPanel(string pluginId, string title, string bodyMarkdown)
    {
        var handle = NewHandle();
        var element = new StubElement(pluginId, "StatusPanel", title, null);
        lock (_lock) { _elements[handle] = element; }
        _log.LogInformation("WpfPluginUIHost STUB: AddStatusPanel plugin={Plugin} handle={Handle} title={Title}",
            pluginId, handle, title);
        return handle;
    }

    public void Update(string handle, string newLabel)
    {
        lock (_lock)
        {
            if (_elements.TryGetValue(handle, out var existing))
            {
                _elements[handle] = existing with { Label = newLabel };
                _log.LogInformation("WpfPluginUIHost STUB: Update handle={Handle} newLabel={NewLabel}", handle, newLabel);
            }
            else
            {
                _log.LogDebug("WpfPluginUIHost STUB: Update on unknown handle={Handle}; ignoring.", handle);
            }
        }
    }

    public void Remove(string handle)
    {
        lock (_lock)
        {
            if (_elements.Remove(handle))
            {
                _log.LogInformation("WpfPluginUIHost STUB: Remove handle={Handle}", handle);
            }
            else
            {
                _log.LogDebug("WpfPluginUIHost STUB: Remove on unknown handle={Handle}; ignoring.", handle);
            }
        }
    }

    private static string NewHandle() => Guid.NewGuid().ToString("N");

    private sealed record StubElement(string PluginId, string Kind, string Label, Action? OnClick);
}
