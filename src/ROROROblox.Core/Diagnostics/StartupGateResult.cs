namespace ROROROblox.Core.Diagnostics;

/// <summary>Acquire-first startup verdict. The mutex is acquired BEFORE this is computed, so the
/// answer is true at the moment RoRoRo proceeds. Mirrors the LaunchResult record-hierarchy pattern.</summary>
public abstract record StartupGateResult
{
    /// <summary>Mutex acquired, no leftover Roblox processes — proceed silently.</summary>
    public sealed record Clean : StartupGateResult;

    /// <summary>Mutex acquired, but leftover Roblox processes exist. Multi-instance is fine; this
    /// is informational. Windowless = safe-to-clean orphans; Windowed = live games the user may
    /// still be playing.</summary>
    public sealed record Leftover(int Windowless, int Windowed) : StartupGateResult;

    /// <summary>Mutex NOT acquired — someone else (the tray-resident Roblox) holds it. Block and
    /// offer recovery.</summary>
    public sealed record Blocked : StartupGateResult;
}
