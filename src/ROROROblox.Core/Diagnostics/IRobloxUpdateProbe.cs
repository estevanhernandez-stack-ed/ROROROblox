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
    /// <c>true</c> when an update is pending for the client that is about to launch. Compares
    /// <c>clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer</c>'s <c>version</c> field
    /// against BOTH the version the <c>roblox-player</c> handler is pinned to
    /// (<c>RobloxCompatChecker.GetHandlerRobloxVersion()</c>) and the newest installed version, and
    /// answers <c>true</c> if EITHER disagrees. Returns <c>false</c> on ANY failure (network, parse,
    /// missing install) — never blocks a launch on a probe error.
    ///
    /// <para><b>The handler version is the load-bearing one (F-104).</b> A launch runs whatever the
    /// handler points at, which during an update is not the newest thing on disk. Reading only the
    /// newest install let the gate answer "no update pending" moments before every client in a batch
    /// self-updated at once.</para>
    /// </summary>
    Task<bool> IsUpdatePendingAsync(CancellationToken ct = default);

    /// <summary>
    /// <c>true</c> when more than one Roblox version was installed in the last few minutes — updates
    /// are landing on top of each other. A reason to hold a batch on its own, independent of any
    /// version comparison, and it costs no network at all.
    ///
    /// <para>Counted on folder CREATION time. Write time moves when a client runs, so counting on it
    /// would report churn during every multilaunch — loudest exactly when it is most wrong.</para>
    /// </summary>
    bool IsUpdateChurnActive();
}
