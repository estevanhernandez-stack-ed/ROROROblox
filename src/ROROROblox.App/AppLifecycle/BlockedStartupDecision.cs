namespace ROROROblox.App.AppLifecycle;

/// <summary>
/// The user's choice on the BLOCKED startup modal (shown when the singleton mutex is held by
/// someone else). See docs/superpowers/specs/2026-07-09-startup-start-anyway-design.md.
/// </summary>
public enum BlockedModalOutcome
{
    /// <summary>Fail-closed default: no explicit choice was made — quit RoRoRo.</summary>
    Quit = 0,

    /// <summary>Close-Roblox-for-me or Retry re-acquired the mutex — proceed holding it.</summary>
    Recovered,

    /// <summary>
    /// Proceed WITHOUT owning the mutex — a benign squatter (another RoRoRo / compatible tool) holds
    /// it, so multi-instance already works. The runtime contested watcher banners the borrowed state.
    /// </summary>
    StartAnyway,
}

/// <summary>
/// Maps a <see cref="BlockedModalOutcome"/> to the startup action. Pure + unit-tested: the
/// load-bearing invariant is that <see cref="BlockedModalOutcome.StartAnyway"/> proceeds WITHOUT the
/// mutex while <see cref="BlockedModalOutcome.Recovered"/> proceeds holding it, and anything
/// unrecognized fails closed to "do not proceed" (quit).
/// </summary>
public static class BlockedStartupDecision
{
    /// <param name="outcome">The modal's outcome.</param>
    /// <returns>
    /// <c>Proceed</c>: continue startup vs. shut down. <c>HoldsMutex</c>: whether RoRoRo owns the
    /// singleton mutex on the proceed path (false for a borrowed "Start anyway" start).
    /// </returns>
    public static (bool Proceed, bool HoldsMutex) Resolve(BlockedModalOutcome outcome) => outcome switch
    {
        BlockedModalOutcome.Recovered => (true, true),
        BlockedModalOutcome.StartAnyway => (true, false),
        _ => (false, false), // Quit + any unrecognized value: fail closed
    };
}
