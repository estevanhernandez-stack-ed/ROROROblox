namespace ROROROblox.Core;

/// <summary>How a read of the session history turned out. Three outcomes, because two of them
/// looked identical on screen for the whole life of the window (F-038).</summary>
public enum SessionHistoryOutcome
{
    /// <summary>Read succeeded and there is something to show.</summary>
    Loaded,

    /// <summary>Read succeeded and the user genuinely has no launches yet.</summary>
    Empty,

    /// <summary>The read failed. The user may well have a long history sitting right there.</summary>
    Unreadable,
}

/// <summary>
/// What the History window says about itself (F-038).
/// <para>
/// THE DEFECT. <c>ReloadAsync</c> caught every read failure into an empty list, and an empty list
/// renders "No launches yet. Click Launch As on any account and you'll see entries here." So a
/// history file that could not be opened — locked by another process, or damaged — presented as a
/// confident statement that the user had never launched anything. The one screen whose entire job
/// is remembering told them there was nothing to remember. Clear had the same shape one step
/// further on: its failure was swallowed into a bare <c>catch</c>, so a clear that did nothing
/// looked exactly like a clear that worked.
/// </para>
/// <para>
/// Diagnostics already had this right — it reports "Collecting…", then either a capture time or
/// "Couldn't collect diagnostics: {message}" — so this is that window's model, moved somewhere
/// both can be read from and one of them can be tested.
/// </para>
/// </summary>
public static class SessionHistoryStatus
{
    /// <summary>The line at the top of the window: what just happened, in one sentence.</summary>
    public static string StatusLine(SessionHistoryOutcome outcome, int count, string? error) => outcome switch
    {
        SessionHistoryOutcome.Unreadable => $"Couldn't read history{Because(error)}",
        SessionHistoryOutcome.Empty => "Nothing recorded yet.",
        _ => count == 1 ? "1 launch recorded." : $"{count} launches recorded.",
    };

    /// <summary>Shown while the read is in flight. Mirrors Diagnostics' "Collecting…".</summary>
    public const string Loading = "Loading…";

    /// <summary>
    /// The centred placeholder that replaces the list. Empty and Unreadable get different words
    /// because they are different facts — that difference is the whole finding.
    /// </summary>
    public static (string Headline, string Detail) Placeholder(SessionHistoryOutcome outcome) => outcome switch
    {
        SessionHistoryOutcome.Unreadable => (
            "History couldn't be read.",
            "The file may be open in another program, or damaged. This doesn't affect your saved "
            + "accounts or settings — they're stored separately."),
        _ => (
            "No launches yet.",
            "Click Launch As on any account and you'll see entries here."),
    };

    /// <summary>Said after a Clear that worked. Previously indistinguishable from one that did not.</summary>
    public const string Cleared = "History cleared.";

    /// <summary>Said after a Clear that failed. Names the one thing the user will want to know.</summary>
    public static string ClearFailed(string? error) => $"Couldn't clear history{Because(error)} Nothing was deleted.";

    /// <summary>
    /// Appends the underlying reason when there is one. The message comes from an exception, so it
    /// is not guaranteed to be a sentence or even non-empty — hence the fallback, rather than
    /// producing "Couldn't read history: ." on a stringly-empty IOException.
    /// </summary>
    private static string Because(string? error) =>
        string.IsNullOrWhiteSpace(error) ? "." : $": {error.Trim()}";
}
