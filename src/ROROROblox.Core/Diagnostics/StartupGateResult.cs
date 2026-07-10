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

    /// <summary>Name NOT won because ROBLOX holds it (as its Event). Multi-instance is genuinely
    /// off until every Roblox process exits. Block and offer recovery.</summary>
    public sealed record Blocked : StartupGateResult;

    /// <summary>
    /// The singleton name is held by another RoRoRo or a compatible multi-instance tool — it exists
    /// as a Mutex, not as Roblox's Event. Roblox is still locked out of its own singleton, so
    /// multi-instance WORKS and there is nothing for the user to recover from. Proceed without
    /// owning the handle; the contested watcher banners the fact that we don't hold it.
    ///
    /// <para>Distinct from <see cref="Blocked"/> deliberately: blocking here threw a modal dialog
    /// at the user for a state that is entirely fine.</para>
    /// </summary>
    public sealed record SharedLock : StartupGateResult;
}
