namespace ROROROblox.Core;

/// <summary>
/// Three-state model for the multi-instance toggle, mirrored by the tray icon.
/// </summary>
public enum MultiInstanceState
{
    /// <summary>Mutex not held; Roblox sees its singleton check normally; only one client can run.</summary>
    Off,

    /// <summary>Mutex held; Roblox's singleton check is defeated; multiple clients can run side by side.</summary>
    On,

    /// <summary>The mutex was held but the OS handle was lost (external close). Multi-instance behavior is undefined.</summary>
    Error,
}
