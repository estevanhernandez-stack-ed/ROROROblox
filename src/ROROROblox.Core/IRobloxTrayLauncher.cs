namespace ROROROblox.Core;

/// <summary>
/// Puts a Roblox client back in the system tray after a seamless takeover, preserving the user's
/// Roblox-at-startup intent. The relaunched client finds RoRoRo already holding the singleton
/// Event, so its own <c>CreateEvent</c> fails and it runs to tray in multi-instance mode alongside
/// RoRoRo (verified 2026-07-10).
/// </summary>
public interface IRobloxTrayLauncher
{
    /// <summary>
    /// Launch <c>RobloxPlayerBeta.exe --launch-to-tray</c>. Best-effort and non-throwing: returns
    /// false when no install is found or the start fails. A false result is not fatal — RoRoRo
    /// still holds the Event and multi-instance still works; the user just has no tray client until
    /// they launch one.
    /// </summary>
    bool RelaunchToTray();
}
