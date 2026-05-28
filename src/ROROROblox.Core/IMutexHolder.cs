namespace ROROROblox.Core;

/// <summary>
/// Owns the OS handle for a named Windows mutex. The product binds this to
/// <c>Local\ROBLOX_singletonEvent</c> — holding it defeats Roblox's single-instance
/// check and enables multiple clients side by side. See spec §5.1.
/// </summary>
public interface IMutexHolder
{
    /// <summary>
    /// The resolved name of the singleton mutex this holder owns (config-driven, falling back to
    /// <c>Local\ROBLOX_singletonEvent</c>). Exposed so the plugin host can hand the same name to a
    /// future add-to-already-running plugin — it must close the exact handle the app holds.
    /// </summary>
    string MutexName { get; }
    bool IsHeld { get; }
    bool Acquire();
    void Release();
    event EventHandler? MutexLost;
}
