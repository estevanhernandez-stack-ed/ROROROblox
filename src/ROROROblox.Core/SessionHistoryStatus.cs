namespace ROROROblox.Core;

/// <summary>How a read of the session history turned out. Three outcomes, because two of them
/// looked identical on screen for the whole life of the window (F-038 — the prose that keeps
/// them distinguishable lives in the App's <c>CoreMessageCatalog</c> since 2026-09-05, per the
/// Core string boundary; this file used to compose it).</summary>
public enum SessionHistoryOutcome
{
    /// <summary>Read succeeded and there is something to show.</summary>
    Loaded,

    /// <summary>Read succeeded and the user genuinely has no launches yet.</summary>
    Empty,

    /// <summary>The read failed. The user may well have a long history sitting right there.</summary>
    Unreadable,
}
