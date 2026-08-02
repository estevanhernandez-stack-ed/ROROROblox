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

    /// <summary>
    /// Shown when the accounts on screen do not all share one FPS cap. Roblox keeps a single
    /// settings file per install, so a differing cap forces RoRoRo to wait for each client to
    /// finish loading before starting the next. Quotes 15 seconds deliberately: the measured
    /// settle is 9-12 s plus the confirm window, and a user told 10 who waits 14 assumes a hang.
    /// </summary>
    public const string FpsCapMismatchBanner =
        "Different FPS caps will slow your launches. Roblox keeps one shared settings file for "
        + "every client, so RoRoRo waits for each account to finish loading before starting the "
        + "next — about 15 seconds each. Set every account to the same cap to launch at full speed.";
}
