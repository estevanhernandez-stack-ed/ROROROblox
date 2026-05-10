namespace ROROROblox.App.Plugins;

/// <summary>
/// Maps each gRPC method name (last path component, no leading slash) to the
/// capability it requires (or null if ungated). The interceptor consults this
/// map for every call: a null value means the method is bootstrap or read-free
/// or downstream-gated; a non-null value means the calling plugin must hold
/// that capability in its consent record.
/// </summary>
public static class RpcMethodCapabilityMap
{
    private static readonly IReadOnlyDictionary<string, string?> Map = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Handshake"] = null,                    // bootstrap — no capability check
        ["GetHostInfo"] = null,                  // free read
        ["GetRunningAccounts"] = null,           // free read (UID-aware)
        ["SubscribeAccountLaunched"] = PluginCapability.HostEventsAccountLaunched,
        ["SubscribeAccountExited"] = PluginCapability.HostEventsAccountExited,
        ["SubscribeMutexStateChanged"] = PluginCapability.HostEventsMutexStateChanged,
        ["RequestLaunch"] = PluginCapability.HostCommandsRequestLaunch,
        ["AddTrayMenuItem"] = PluginCapability.HostUITrayMenu,
        ["AddRowBadge"] = PluginCapability.HostUIRowBadge,
        ["AddStatusPanel"] = PluginCapability.HostUIStatusPanel,
        ["UpdateUI"] = null,                     // gated by handle ownership downstream
        ["RemoveUI"] = null,                     // gated by handle ownership downstream
    };

    public static string? Required(string methodName)
    {
        return Map.TryGetValue(methodName, out var cap) ? cap : null;
    }

    public static bool IsKnown(string methodName) => Map.ContainsKey(methodName);

    /// <summary>
    /// Extract the trailing method name from a full Grpc method path
    /// (e.g. "/rororo.plugin.v1.RoRoRoHost/RequestLaunch" → "RequestLaunch").
    /// </summary>
    public static string ExtractMethodName(string fullMethod)
    {
        if (string.IsNullOrEmpty(fullMethod)) return string.Empty;
        var lastSlash = fullMethod.LastIndexOf('/');
        return lastSlash >= 0 && lastSlash < fullMethod.Length - 1
            ? fullMethod[(lastSlash + 1)..]
            : fullMethod;
    }
}
