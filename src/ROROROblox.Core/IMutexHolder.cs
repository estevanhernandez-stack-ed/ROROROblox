namespace ROROROblox.Core;

/// <summary>
/// Owns the OS handle for a named Windows mutex. The product binds this to
/// <c>Local\ROBLOX_singletonEvent</c> — holding it defeats Roblox's single-instance
/// check and enables multiple clients side by side. See spec §5.1.
/// </summary>
public interface IMutexHolder
{
    bool IsHeld { get; }
    bool Acquire();
    void Release();
    event EventHandler? MutexLost;
}
