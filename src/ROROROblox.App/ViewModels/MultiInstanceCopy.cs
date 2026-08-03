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
    /// finish loading before starting the next. Quotes 20 seconds deliberately: the proof-of-read
    /// wait (2026-08-02) means a contended attempt can now run 15-20 s, plus MainViewModel's 5 s
    /// InterLaunchThrottle between hops — promising 20 and delivering sooner is the right
    /// direction to be wrong in, same rationale as the original 15 s figure, updated for the new
    /// ceiling.
    /// </summary>
    public const string FpsCapMismatchBanner =
        "Different FPS caps will slow your launches. Roblox keeps one shared settings file for "
        + "every client, so RoRoRo waits for each account to finish loading before starting the "
        + "next — up to about 20 seconds each. Set every account to the same cap to launch at full speed.";
}
