namespace ROROROblox.App.ViewModels;

/// <summary>
/// What a batch launch should do given the install-deferral signals. The v1.7.0 pre-warm gate
/// (install-deferral spec §"Components > 2. Pre-warm batch launch" + "3. Version pre-check").
/// </summary>
internal enum PreWarmDecision
{
    /// <summary>
    /// Launch the whole batch as today — normal multilaunch speed, no pre-warm wait. The common
    /// path: either a strap owns the handler and self-updates Roblox, OR no update is pending.
    /// </summary>
    LaunchAllNow,

    /// <summary>
    /// An update is pending and no strap is handling launches — launch the FIRST eligible account,
    /// wait for the update to clear, then release the rest of the batch. Bloxstrap's
    /// serialize-the-update behavior at RoRoRo's layer.
    /// </summary>
    PreWarmThenRelease,
}

/// <summary>
/// Pure gating logic for the v1.7.0 install-deferral pre-warm. Lifted out of
/// <see cref="MainViewModel"/> so the decision + the wait-complete predicate are unit-testable
/// without the heavy view model or a live process / network. The VM wires the two probes
/// (<c>IBloxstrapDetector.IsStrapHandlingLaunches</c> + <c>IRobloxUpdateProbe</c>) into these and
/// owns the actual timing/await loop (verify-by-running). Spec §"Data flow".
/// </summary>
internal static class PreWarmGate
{
    /// <summary>
    /// The hard upper bound on the pre-warm wait. Same family as the v1.6.0 AppStorageDefender
    /// max-cap and the item-6 tracker deadline — an install box can postpone the first RPB's
    /// attach well past the old windows, so the wait matches the 120s install-delay ceiling. On
    /// hitting the cap the VM releases the rest of the batch best-effort (never hangs forever).
    /// </summary>
    public static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Decide whether to pre-warm. Strap-handling short-circuits regardless of the update signal
    /// (the strap updates Roblox proactively itself, so pre-warming would double-update). Otherwise
    /// pre-warm only when an update is actually pending — the common no-update path stays full speed.
    /// </summary>
    /// <param name="strapHandling">
    /// <c>IBloxstrapDetector.IsStrapHandlingLaunches()</c> — Bloxstrap/Fishstrap owns the handler.
    /// </param>
    /// <param name="updatePending">
    /// <c>IRobloxUpdateProbe.IsUpdatePendingAsync()</c> — the installed version differs from latest.
    /// </param>
    public static PreWarmDecision Decide(bool strapHandling, bool updatePending)
    {
        if (strapHandling)
        {
            // A strap is the handler — it serializes its own update before launching. Don't
            // double-update; release the batch at normal speed (spec Riders §7).
            return PreWarmDecision.LaunchAllNow;
        }

        return updatePending
            ? PreWarmDecision.PreWarmThenRelease
            : PreWarmDecision.LaunchAllNow;
    }

    /// <summary>
    /// True when the pre-warm wait is done: the installer process is gone AND the first launched
    /// account has attached (its <c>RobloxPlayerBeta</c> is up). Once both hold, the remaining
    /// version matches the installed version, so the rest of the batch never re-triggers the
    /// installer. Either condition false → keep waiting (until the VM's <see cref="MaxWait"/> cap).
    /// </summary>
    /// <param name="installerRunning"><c>IRobloxUpdateProbe.IsInstallerRunning()</c> — install in progress.</param>
    /// <param name="firstAttached">The first account's <c>summary.IsRunning</c> — its RPB attached.</param>
    public static bool PreWarmWaitComplete(bool installerRunning, bool firstAttached)
        => !installerRunning && firstAttached;
}
