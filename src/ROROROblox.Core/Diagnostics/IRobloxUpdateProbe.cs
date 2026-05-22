namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Detects whether a Roblox client update is in progress or pending — the foundational signal the
/// v1.7.0 install-deferral lane consumes (spec §"Components > 1. Update-pending detection").
/// Posture-clean: one process check + one documented GET, no bootstrapper / handler takeover.
/// </summary>
/// <remarks>
/// Both members are <b>degrade-safe</b>: on ANY failure (network, parse, missing install, scan
/// exception) they return the "don't block the launch" answer (<c>false</c>). The probe must never
/// be the reason a launch is blocked — a false negative (probe says "no update" when one is pending)
/// degrades to today's behavior (the launch hits Roblox's reactive installer); a false positive
/// would needlessly stall the batch. We bias to the recoverable failure.
/// </remarks>
public interface IRobloxUpdateProbe
{
    /// <summary>
    /// <c>true</c> when <c>RobloxPlayerInstaller.exe</c> is currently running — an update is
    /// installing right now. Same process-scan family as <c>RobloxProcessTracker</c>. Returns
    /// <c>false</c> if the scan throws (degrade-safe).
    /// </summary>
    bool IsInstallerRunning();

    /// <summary>
    /// <c>true</c> when the installed Roblox version differs from the latest published version
    /// (an update is pending pre-launch). Compares <c>RobloxCompatChecker.GetInstalledRobloxVersion()</c>
    /// against <c>clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer</c>'s <c>version</c>
    /// field. Returns <c>false</c> on ANY failure (network, parse, missing install) — never blocks
    /// a launch on a probe error.
    /// </summary>
    Task<bool> IsUpdatePendingAsync(CancellationToken ct = default);
}
