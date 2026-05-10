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
    public const string HostCommandsRequestLaunch = "host.commands.request-launch";
    public const string HostUITrayMenu = "host.ui.tray-menu";
    public const string HostUIRowBadge = "host.ui.row-badge";
    public const string HostUIStatusPanel = "host.ui.status-panel";

    public const string SystemSynthesizeKeyboardInput = "system.synthesize-keyboard-input";
    public const string SystemSynthesizeMouseInput = "system.synthesize-mouse-input";
    public const string SystemWatchGlobalInput = "system.watch-global-input";
    public const string SystemPreventSleep = "system.prevent-sleep";
    public const string SystemFocusForeignWindows = "system.focus-foreign-windows";

    private static readonly IReadOnlyDictionary<string, string> Catalog = new Dictionary<string, string>
    {
        [HostEventsAccountLaunched] = "Notify the plugin when an account launches.",
        [HostEventsAccountExited] = "Notify the plugin when an account exits.",
        [HostEventsMutexStateChanged] = "Notify the plugin when multi-instance state changes.",
        [HostCommandsRequestLaunch] = "Allow the plugin to ask RoRoRo to launch a Roblox account.",
        [HostUITrayMenu] = "Allow the plugin to add tray menu items.",
        [HostUIRowBadge] = "Allow the plugin to add a badge on each saved-account row.",
        [HostUIStatusPanel] = "Allow the plugin to add a status panel to the main window.",
        [SystemSynthesizeKeyboardInput] = "The plugin will synthesize keyboard input on your machine.",
        [SystemSynthesizeMouseInput] = "The plugin will synthesize mouse input on your machine.",
        [SystemWatchGlobalInput] = "The plugin will watch your keyboard + mouse input system-wide.",
        [SystemPreventSleep] = "The plugin will prevent your computer from sleeping while it runs.",
        [SystemFocusForeignWindows] = "The plugin will activate / focus other applications' windows.",
    };

    public static bool IsKnown(string capability)
        => !string.IsNullOrEmpty(capability) && Catalog.ContainsKey(capability);

    public static bool IsHostEnforced(string capability)
        => IsKnown(capability) && capability.StartsWith("host.", StringComparison.Ordinal);

    public static string Display(string capability)
        => Catalog.TryGetValue(capability, out var explanation)
            ? explanation
            : $"Unknown capability: {capability}";
}
