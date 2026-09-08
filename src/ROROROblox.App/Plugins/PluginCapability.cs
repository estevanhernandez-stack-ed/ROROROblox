using ROROROblox.App.Localization;

namespace ROROROblox.App.Plugins;

/// <summary>
/// The capability vocabulary plugins declare in their manifest. Two namespaces:
/// <list type="bullet">
///   <item><c>host.*</c> — what the plugin asks RoRoRo for. Gated by gRPC interceptor on every call.</item>
///   <item><c>system.*</c> — what the plugin does locally on the user's machine. Disclosed for consent
///   but not enforced by RoRoRo (the plugin runs as its own process; we can't sandbox it).</item>
/// </list>
/// </summary>
public static class PluginCapability
{
    public const string HostEventsAccountLaunched = "host.events.account-launched";
    public const string HostEventsAccountExited = "host.events.account-exited";
    public const string HostEventsMutexStateChanged = "host.events.mutex-state-changed";
    public const string HostEventsMemoryPressure = "host.events.memory-pressure";
    public const string HostCommandsRequestLaunch = "host.commands.request-launch";
    public const string HostCommandsLaunchTarget = "host.commands.launch-target";
    public const string HostCommandsMarkAccountActive = "host.commands.mark-account-active";
    public const string HostCommandsStopAccounts = "host.commands.stop-accounts";
    public const string HostQueriesCurrentServer = "host.queries.current-server";
    public const string HostQueriesAccountActivity = "host.queries.account-activity";
    public const string HostQueriesAccounts = "host.queries.accounts";
    public const string HostUITrayMenu = "host.ui.tray-menu";
    public const string HostUIRowBadge = "host.ui.row-badge";
    public const string HostUIStatusPanel = "host.ui.status-panel";

    public const string SystemSynthesizeKeyboardInput = "system.synthesize-keyboard-input";
    public const string SystemSynthesizeMouseInput = "system.synthesize-mouse-input";
    public const string SystemWatchGlobalInput = "system.watch-global-input";
    public const string SystemPreventSleep = "system.prevent-sleep";
    public const string SystemFocusForeignWindows = "system.focus-foreign-windows";
    public const string SystemReadScreen = "system.read-screen";

    // Maps each capability to its resx KEY, not its English sentence. Display() resolves the key
    // through Loc at CALL time so the consent sheet — an on-demand modal — reads the current UI
    // culture when it is shown, rather than freezing whatever culture was current when this static
    // dictionary first initialized (localization Phase D step 3 batch 13, mirrors CaptionColorPicker).
    private static readonly IReadOnlyDictionary<string, string> ResxKeys = new Dictionary<string, string>
    {
        [HostEventsAccountLaunched] = "Plugin_Cap_AccountLaunched",
        [HostEventsAccountExited] = "Plugin_Cap_AccountExited",
        [HostEventsMutexStateChanged] = "Plugin_Cap_MutexStateChanged",
        [HostEventsMemoryPressure] = "Plugin_Cap_MemoryPressure",
        [HostCommandsRequestLaunch] = "Plugin_Cap_RequestLaunch",
        [HostCommandsLaunchTarget] = "Plugin_Cap_LaunchTarget",
        [HostCommandsMarkAccountActive] = "Plugin_Cap_MarkAccountActive",
        [HostCommandsStopAccounts] = "Plugin_Cap_StopAccounts",
        [HostQueriesCurrentServer] = "Plugin_Cap_CurrentServer",
        [HostQueriesAccountActivity] = "Plugin_Cap_AccountActivity",
        [HostQueriesAccounts] = "Plugin_Cap_Accounts",
        [HostUITrayMenu] = "Plugin_Cap_TrayMenu",
        [HostUIRowBadge] = "Plugin_Cap_RowBadge",
        [HostUIStatusPanel] = "Plugin_Cap_StatusPanel",
        [SystemSynthesizeKeyboardInput] = "Plugin_Cap_SynthKeyboard",
        [SystemSynthesizeMouseInput] = "Plugin_Cap_SynthMouse",
        [SystemWatchGlobalInput] = "Plugin_Cap_WatchGlobalInput",
        [SystemPreventSleep] = "Plugin_Cap_PreventSleep",
        [SystemFocusForeignWindows] = "Plugin_Cap_FocusForeignWindows",
        [SystemReadScreen] = "Plugin_Cap_ReadScreen",
    };

    public static bool IsKnown(string capability)
        => !string.IsNullOrEmpty(capability) && ResxKeys.ContainsKey(capability);

    public static bool IsHostEnforced(string capability)
        => IsKnown(capability) && capability.StartsWith("host.", StringComparison.Ordinal);

    public static string Display(string capability)
        => ResxKeys.TryGetValue(capability, out var resxKey)
            ? Loc.Get(resxKey)
            : Loc.Format("Plugin_Cap_Unknown", capability);
}
