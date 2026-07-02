namespace ROROROblox.App.ViewModels;

/// <summary>User-facing copy for the multi-instance lock states (spec §5/§6). Centralized so the
/// startup modal (Task 6) and the runtime banner (Task 8) share exact strings.</summary>
public static class MultiInstanceCopy
{
    /// <summary>Runtime banner shown when Roblox holds the lock post-startup.</summary>
    public const string ContestedBanner =
        "Roblox has the multi-instance lock — it's probably running in your system tray.";

    /// <summary>Tick shown in the BLOCKED modal after a Retry that still failed.</summary>
    public const string StillLocked = "Still locked — Roblox is still running.";
}
