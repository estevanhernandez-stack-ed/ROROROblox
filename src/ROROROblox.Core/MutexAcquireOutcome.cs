namespace ROROROblox.Core;

/// <summary>
/// Why an attempt on the singleton name succeeded or failed. The two failure modes mean opposite
/// things for the user, and collapsing them into a bare <c>false</c> is what made RoRoRo show the
/// same blocking modal for both.
///
/// <para><b>How the trick actually works.</b> Roblox creates a kernel <i>Event</i> named
/// <c>Local\ROBLOX_singletonEvent</c>. RoRoRo squats that same name with a <i>Mutex</i>. Whoever
/// creates the name first wins it, and the loser's create call fails because the object already
/// exists under a different type. When RoRoRo wins, Roblox's own <c>CreateEvent</c> fails and it
/// never installs its single-instance enforcement — that is what lets clients run side by side.</para>
///
/// <para>So the error code tells you exactly who holds the name:</para>
/// <list type="bullet">
///   <item><c>ERROR_INVALID_HANDLE</c> — the name exists as a NON-mutex object. That is Roblox's
///   Event. Multi-instance is genuinely unavailable until every Roblox process exits and the
///   Event's last handle closes.</item>
///   <item><c>ERROR_ALREADY_EXISTS</c> — the name exists as a Mutex, so something created it the
///   way RoRoRo does: another copy of RoRoRo, or a compatible multi-instance tool. Roblox still
///   loses the name, so multi-instance keeps working and there is nothing to recover from.</item>
/// </list>
///
/// <para>Verified on 2026-07-10 against a tray-resident Roblox client (no game running):
/// <c>OpenMutex</c> → <c>ERROR_INVALID_HANDLE</c>, <c>OpenEvent</c> → success,
/// <c>CreateMutex</c> → <c>ERROR_INVALID_HANDLE</c>. The tray client takes the singleton at
/// process start, not at game launch.</para>
/// </summary>
public enum MutexAcquireOutcome
{
    /// <summary>We created and now own the name. Multi-instance is on.</summary>
    Acquired,

    /// <summary>
    /// The name exists as a non-mutex object — Roblox's Event. Multi-instance is off until Roblox
    /// fully exits. This is the case the BLOCKED modal exists for.
    /// </summary>
    HeldByRoblox,

    /// <summary>
    /// The name exists as a Mutex: another RoRoRo or a compatible multi-instance tool holds it.
    /// Roblox is still locked out of its own singleton, so multi-instance works and startup should
    /// NOT block. We simply don't own the handle.
    /// </summary>
    HeldByCompatibleTool,

    /// <summary>CreateMutex failed for a reason that isn't one of the above.</summary>
    Failed,
}
