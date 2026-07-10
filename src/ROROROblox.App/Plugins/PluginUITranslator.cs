using ROROROblox.PluginContract;

namespace ROROROblox.App.Plugins;

/// <summary>
/// Translates plugin UI requests (proto specs) into IPluginUIHost calls and
/// tracks owner-by-handle so a plugin cannot remove or mutate UI it didn't
/// create. Click handlers attached to host-side menu items are converted back
/// into <see cref="UIInteractionEvent"/> emissions via the
/// <see cref="UIInteraction"/> event — the App layer wires that event to the
/// per-plugin gRPC client (Plugin.OnUIInteraction).
///
/// Note on click-handler binding: the host returns the handle id only AFTER
/// AddTrayMenuItem returns, but the click callback must carry that same id.
/// The translator captures the id into a closure variable that is filled in
/// once the host call returns. Pre-callback clicks (impossible in practice,
/// since UI elements aren't visible until the host returns) are no-ops.
/// </summary>
public sealed class PluginUITranslator
{
    private readonly IPluginUIHost _host;
    private readonly Dictionary<string, string> _ownerByHandle = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public PluginUITranslator(IPluginUIHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// Raised when a tray menu item created by a plugin is clicked. The App
    /// layer subscribes and forwards the event to the per-plugin gRPC client
    /// via the Plugin.OnUIInteraction RPC.
    /// </summary>
    public event Action<string /*pluginId*/, UIInteractionEvent>? UIInteraction;

    public UIHandle AddTrayMenuItem(string pluginId, MenuItemSpec spec)
    {
        // Deferred handle capture — onClick reads the handle id assigned on return.
        string? capturedHandleId = null;
        var handleId = _host.AddTrayMenuItem(pluginId, spec.Label, spec.Tooltip, spec.Enabled,
            onClick: () =>
            {
                if (capturedHandleId is null) return;
                UIInteraction?.Invoke(pluginId, new UIInteractionEvent
                {
                    Handle = new UIHandle { Id = capturedHandleId },
                    InteractionKind = "click",
                    TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
            });
        capturedHandleId = handleId;
        lock (_lock) _ownerByHandle[handleId] = pluginId;
        return new UIHandle { Id = handleId };
    }

    public UIHandle AddRowBadge(string pluginId, RowBadgeSpec spec)
    {
        var handleId = _host.AddRowBadge(pluginId, spec.Text, spec.ColorHex, spec.Tooltip);
        lock (_lock) _ownerByHandle[handleId] = pluginId;
        return new UIHandle { Id = handleId };
    }

    public UIHandle AddStatusPanel(string pluginId, StatusPanelSpec spec)
    {
        var handleId = _host.AddStatusPanel(pluginId, spec.Title, spec.BodyMarkdown);
        lock (_lock) _ownerByHandle[handleId] = pluginId;
        return new UIHandle { Id = handleId };
    }

    /// <summary>
    /// True when <paramref name="pluginId"/> created the handle and it is still live.
    /// Ownership is the ONLY gate on UpdateUI/RemoveUI — the capability map deliberately
    /// leaves both ungated (RpcMethodCapabilityMap maps them to null), so the service
    /// layer must refuse on false rather than assume the interceptor covered it.
    /// </summary>
    public bool OwnsHandle(string pluginId, string handleId)
    {
        if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(handleId)) return false;
        lock (_lock)
        {
            return _ownerByHandle.TryGetValue(handleId, out var owner) && owner == pluginId;
        }
    }

    /// <summary>
    /// Removes a UI element. Returns false — removing nothing — when the handle is
    /// unknown or the caller does not own it, so the service layer can refuse the RPC.
    /// One outcome for both cases: callers must not be able to probe which handle
    /// ids exist.
    /// </summary>
    public bool RemoveUI(string pluginId, UIHandle handle)
    {
        if (string.IsNullOrEmpty(pluginId) || string.IsNullOrEmpty(handle.Id)) return false;
        bool ownerMatches;
        lock (_lock)
        {
            ownerMatches = _ownerByHandle.TryGetValue(handle.Id, out var owner) && owner == pluginId;
            if (ownerMatches) _ownerByHandle.Remove(handle.Id);
        }
        if (ownerMatches) _host.Remove(handle.Id);
        return ownerMatches;
    }
}
