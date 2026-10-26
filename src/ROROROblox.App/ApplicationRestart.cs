using Microsoft.Extensions.Logging;
using Windows.Win32.System.Recovery;

namespace ROROROblox.App;

/// <summary>
/// Registers RoRoRo with Windows so it comes back after a crash, a hang or a Store update (#246).
/// </summary>
internal static class ApplicationRestart
{
    /// <summary>
    /// <c>RESTART_NO_REBOOT</c> (8) only. Crash and hang restart (WER) and patch/servicing restart
    /// (Restart Manager) stay allowed, which is the point of #246 after the field crashes of
    /// 2026-10-24. Reboot restart is off because run-on-login is opt-in in RoRoRo and off on the
    /// field machine: restarting after a Windows Update reboot would quietly autostart the app.
    /// WER also requires 60 seconds of uptime before it restarts, so a crash-at-startup cannot loop.
    /// Pinned by <c>ApplicationRestartFlagsTests</c>.
    /// </summary>
    internal const uint Flags = 8;

    /// <summary>Registers once for restart. Never throws; the result is logged at Debug.</summary>
    internal static void Register(ILogger log)
    {
        try
        {
            var hr = Windows.Win32.PInvoke.RegisterApplicationRestart(
                default, (REGISTER_APPLICATION_RESTART_FLAGS)Flags);
            log.LogDebug("RegisterApplicationRestart returned HRESULT 0x{Hr:X8}.", hr.Value);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "RegisterApplicationRestart failed; continuing without restart registration.");
        }
    }
}
