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
/// shipped ungated (PR #60). <see cref="AssertExhaustive"/> throws on a missing
/// entry, so the hole can never be served.</para>
///
/// <para><b>Where the failure is loud.</b> This comment used to say AssertExhaustive
/// turns a missing entry into a startup crash. It does not, and as of v1.23.0.0
/// (2026-08-30) it never has in production: <c>App.StartPluginHostListener</c> runs
/// <see cref="PluginHostStartupService.StartAsync"/> fire-and-forget and its
/// continuation logs the faulted task at Debug, so a missing entry means plugins are
/// silently disabled for the session and autostart skips. Nothing the user sees. The
/// gate that actually goes red is the two tests that call AssertExhaustive directly:
/// <c>RpcMethodCapabilityMapTests.EveryRoRoRoHostMethod_HasACapabilityMapEntry</c>
/// and the harness's <c>CapabilityMap_CoversEveryHostMethod</c>.</para>
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
        ["SubscribeMemoryPressure"] = PluginCapability.HostEventsMemoryPressure,
        // Theming (0.8.0). Both ungated, deliberately, and SubscribeThemeChanged is the FIRST
        // ungated stream on this service -- every other Subscribe* requires a capability and
        // every ungated entry above is a one-shot read. Worth stating rather than inheriting.
        //
        // Capabilities fence what can cause harm. A colour cannot: the feed carries eleven hex
        // codes, no account data, no identity, no host state. Gating it would mean a user can
        // decline a plugin's ability to LOOK CORRECT, which is a worse outcome than any risk of
        // knowing what #101010 is -- and a plugin denied the feed would fall back to painting
        // itself the wrong colour, which is the exact defect this contract addition removes.
        ["GetTheme"] = null,                     // free read
        ["SubscribeThemeChanged"] = null,        // free stream -- see above
        ["RequestLaunch"] = PluginCapability.HostCommandsRequestLaunch,
        ["RequestLaunchTarget"] = PluginCapability.HostCommandsLaunchTarget,
        ["GetCurrentServer"] = PluginCapability.HostQueriesCurrentServer,
        ["GetAccountActivity"] = PluginCapability.HostQueriesAccountActivity,
        ["GetAccounts"] = PluginCapability.HostQueriesAccounts,
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
    /// Every method on the RoRoRoHost service must have an entry here. Throws when one is
    /// missing. Called from <see cref="PluginHostStartupService.StartAsync"/> before the pipe
    /// binds, and from the two exhaustiveness tests named in the class summary. In the app the
    /// throw disables plugins for the session (logged at Debug); it is the tests, not startup,
    /// that make a forgotten entry visible. See the class summary for the correction.
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
