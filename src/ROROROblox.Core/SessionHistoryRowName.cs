namespace ROROROblox.Core;

/// <summary>
/// The one sentence a screen reader should hear for a session, instead of five loose fragments
/// (F-072).
/// <para>
/// WHAT WAS THERE. The History window renders each session as five separate <c>TextBlock</c>s
/// inside an unnamed <c>Border</c>, and the automation tree showed it: <b>503 unpaired Text nodes
/// across 100 sessions, and not a single container carrying a name.</b> A screen reader walking that
/// gets "estehernandez", "Pet Sim", "4:57 PM", "1 min", "Saved" as five unrelated announcements, and
/// has to hold five things in its listener's head to reassemble one row — a hundred times over.
/// Sighted users get the grouping for free from geometry; that is the whole reason it survived.
/// </para>
/// <para>
/// WHAT THIS DOES NOT DO. It does not make History a real list. Genuine <c>DataItem</c> semantics
/// mean an <c>ItemsControl</c> and a rebuild, and F-072 says in as many words that the row is
/// "placement evidence only, not a rebuild". Naming the container is the part that pays now: the
/// fragments stop being orphans and each row announces itself once, in the order it reads on screen.
/// </para>
/// <para>
/// It also does not invent a hundred tab stops. Rows are named, not focusable — a list where Tab
/// visits every row is a worse keyboard experience than one where it visits none, and the right
/// answer there is arrow-key navigation inside a real list control, which is the rebuild.
/// </para>
/// </summary>
public static class SessionHistoryRowName
{
    /// <summary>
    /// Composes the row's spoken name from exactly what the row shows, in the order it shows it.
    /// </summary>
    /// <param name="displayName">The account as rendered — already streamer-mode substituted, so a
    /// fake identity on screen is a fake identity in the announcement. Reading the real name aloud
    /// while the screen shows an alias would defeat streamer mode through the accessibility tree.</param>
    /// <param name="gameName">Null or empty becomes the same "(unknown game)" the row displays.</param>
    /// <param name="isPrivateServer">Rendered as a PRIVATE badge, which is silent without this.</param>
    /// <param name="startedAtLocal">Local start time, formatted as the row formats it.</param>
    /// <param name="duration">The row's own duration text — "1 min", "&lt;1 min", "still running".
    /// Passed in rather than recomputed so the name cannot drift from the pixels.</param>
    /// <param name="outcomeHint">Optional trailing note the row appends after the game.</param>
    /// <param name="isSaved">Whether the game is bookmarked, which the row shows as "Saved".</param>
    public static string Compose(
        string displayName,
        string? gameName,
        bool isPrivateServer,
        string startedAtLocal,
        string duration,
        string? outcomeHint,
        bool isSaved)
    {
        var parts = new List<string>
        {
            string.IsNullOrWhiteSpace(displayName) ? "Unknown account" : displayName.Trim(),
            string.IsNullOrWhiteSpace(gameName) ? "(unknown game)" : gameName.Trim(),
        };

        if (isPrivateServer) parts.Add("private server");

        if (!string.IsNullOrWhiteSpace(startedAtLocal)) parts.Add($"started {startedAtLocal.Trim()}");
        if (!string.IsNullOrWhiteSpace(duration)) parts.Add(duration.Trim());
        if (!string.IsNullOrWhiteSpace(outcomeHint)) parts.Add(outcomeHint.Trim());

        // Only when true. "Not saved" would be noise on every unbookmarked row, and the row itself
        // says nothing in that case either — it shows a "+ Bookmark" button, which announces itself.
        if (isSaved) parts.Add("saved");

        return string.Join(", ", parts) + ".";
    }
}
