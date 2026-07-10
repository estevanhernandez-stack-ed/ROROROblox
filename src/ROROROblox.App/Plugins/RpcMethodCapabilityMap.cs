namespace ROROROblox.App.Plugins;

/// <summary>
/// Maps each gRPC method name (last path component, no leading slash) to the
/// capability it requires (or null if ungated). The interceptor consults this
/// map for every call: a null value means the method is bootstrap or read-free
/// or downstream-gated; a non-null value means the calling plugin must hold
/// that capability in its consent record.
///
/// <para><b>Absence is not permission.</b> A method missing from this map is
/// <em>unknown</em>, not ungated — <see cref="CapabilityInterceptor"/> denies it.
/// That distinction is why <see cref="IsKnown"/> exists alongside
/// <see cref="Required"/>: <c>Required</c> returning null is ambiguous on its own,
/// and reading it as "no capability needed" is exactly how UpdateUI and RemoveUI
/// shipped ungated (PR #60). <see cref="AssertExhaustive"/> makes the failure
/// mode a startup crash instead of a silent hole.</para>
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
        ["RequestLaunchTarget"] = PluginCapability.HostCommandsLaunchTarget,
        ["GetCurrentServer"] = PluginCapability.HostQueriesCurrentServer,
        ["GetAccountActivity"] = PluginCapability.HostQueriesAccountActivity,
        ["MarkAccountActive"] = PluginCapability.HostCommandsMarkAccountActive,
        ["StopAccounts"] = PluginCapability.HostCommandsStopAccounts,
        ["AddTrayMenuItem"] = PluginCapability.HostUITrayMenu,
        ["AddRowBadge"] = PluginCapability.HostUIRowBadge,
        ["AddStatusPanel"] = PluginCapability.HostUIStatusPanel,
        ["UpdateUI"] = null,                     // gated by handle ownership downstream
        ["RemoveUI"] = null,                     // gated by handle ownership downstream
    };

    /// <summary>
    /// The capability <paramref name="methodName"/> requires, or null when it is ungated.
    /// Also returns null for an UNKNOWN method — callers must check <see cref="IsKnown"/>
    /// first, or use <see cref="TryGetRequired"/>, which collapses the ambiguity.
    /// </summary>
    public static string? Required(string methodName)
    {
        return Map.TryGetValue(methodName, out var cap) ? cap : null;
    }

    /// <summary>
    /// True when the method appears in the map at all, gated or not. False means the
    /// method is unrecognized and must be denied.
    /// </summary>
    public static bool IsKnown(string methodName) => Map.ContainsKey(methodName);

    /// <summary>
    /// Unambiguous lookup. Returns false when the method is unknown (deny). Returns true
    /// when it is known, with <paramref name="capability"/> set to the required capability
    /// or null when the method is deliberately ungated.
    /// </summary>
    public static bool TryGetRequired(string methodName, out string? capability)
        => Map.TryGetValue(methodName, out capability);

    /// <summary>
    /// Every method on the RoRoRoHost service must have an entry here. Called at host
    /// startup so a method added to the .proto but forgotten in this map crashes the app
    /// instead of shipping ungated.
    /// </summary>
    public static void AssertExhaustive()
    {
        var service = PluginContract.PluginContractReflection.Descriptor.Services
            .FirstOrDefault(s => s.Name == "RoRoRoHost")
            ?? throw new InvalidOperationException("RoRoRoHost service not found in the generated descriptor.");

        var missing = service.Methods
            .Select(m => m.Name)
            .Where(name => !Map.ContainsKey(name))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"RpcMethodCapabilityMap is missing entries for: {string.Join(", ", missing)}. " +
                "Every RoRoRoHost method needs an entry — use null for a deliberately ungated method.");
        }
    }

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
