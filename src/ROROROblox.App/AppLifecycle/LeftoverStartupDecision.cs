namespace ROROROblox.App.AppLifecycle;

/// <summary>What startup should do about leftover Roblox processes.</summary>
public enum LeftoverDisposition
{
    /// <summary>Nothing to do.</summary>
    Nothing = 0,

    /// <summary>Clear the strays and carry on. No dialog: nothing is at stake and it happens every time.</summary>
    ClearSilently,

    /// <summary>Ask. A windowed client may be a live session with something in it.</summary>
    Ask,
}

/// <summary>
/// Decides whether leftover Roblox processes are worth interrupting the user about (F-110).
/// <para>
/// WHY THIS EXISTS. Stopping a client CLEANLY spawns a fresh headless <c>RobloxPlayerBeta</c> about
/// two seconds later — Roblox returning to its own app shell. Measured 2026-08-20: baseline zero
/// clients, one launched and stopped, and a new pid appears with <c>hwnd=0</c>, no title, ~120-180
/// MB, still there minutes later and growing slowly. That is not an incident, it is the
/// deterministic aftermath of every normal session.
/// </para>
/// <para>
/// The old gate counted it as a leftover and raised a dialog. So the dialog fired on the ordinary
/// path — play, stop, reopen RoRoRo, get a modal — and the user learned to click through it without
/// reading. That is the failure this fixes, and it is not cosmetic: the BLOCKED modal sits in the
/// same position and is the one that genuinely matters, and it inherits the same reflex.
/// </para>
/// <para>
/// A WINDOWED leftover is a different thing entirely and still asks. It has a game window, which
/// means it may have a session in it, and RoRoRo does not get to close that on the user's behalf
/// without a word — the existing dialog and its Stop-all confirm stay exactly as they were.
/// </para>
/// </summary>
public static class LeftoverStartupDecision
{
    /// <param name="windowless">Headless clients — the clean-exit remnant described above.</param>
    /// <param name="windowed">Clients with a real window, which may hold a live session.</param>
    /// <returns>
    /// <see cref="LeftoverDisposition.Ask"/> whenever ANY windowed client is present, even
    /// alongside windowless ones — the windowed one is the reason to ask, and a silent sweep that
    /// happened to take a live game with it would be far worse than a dialog too many.
    /// </returns>
    public static LeftoverDisposition Decide(int windowless, int windowed)
    {
        if (windowed > 0) return LeftoverDisposition.Ask;
        if (windowless > 0) return LeftoverDisposition.ClearSilently;
        return LeftoverDisposition.Nothing;
    }
}
