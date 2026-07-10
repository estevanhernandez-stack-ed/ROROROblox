namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// The safety gate for silently taking the singleton name back from Roblox at startup.
///
/// <para>Roblox's tray client (Windows-startup, <c>--launch-to-tray</c>) is windowless and holds
/// the singleton Event, which blocks RoRoRo's multi-instance. Because a tray client has no game
/// window, it has nothing to lose — RoRoRo can close it, reclaim the name, and put a tray client
/// back, with no modal. But a <b>windowed</b> client may be mid-game, and closing it would throw
/// away unsaved progress. That case must stay behind the confirming BLOCKED modal.</para>
/// </summary>
public static class SeamlessTakeover
{
    /// <summary>
    /// True only when Roblox is running AND every client is windowless. A single windowed client
    /// makes this false — its presence forces the confirming modal instead of a silent close.
    /// An empty list is false: there is nothing to take over, and the caller should not have been
    /// blocked at all.
    /// </summary>
    public static bool WindowlessOnly(IReadOnlyList<RobloxProcessInfo> players)
        => players.Count > 0 && players.All(p => !p.HasWindow);
}
