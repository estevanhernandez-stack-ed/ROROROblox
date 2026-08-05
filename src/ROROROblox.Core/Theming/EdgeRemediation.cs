namespace ROROROblox.Core.Theming;

/// <summary>
/// Decides whether to ask a theme's author before changing how their theme looks, and what to
/// render once they have answered.
/// <para>
/// Wave 5 gives interactive controls an edge derived to clear WCAG 1.4.11's 3:1. For a built-in
/// theme that is simply a bug fix. For a theme somebody wrote themselves it is us altering their
/// work, so it gets asked once rather than done silently — Este's call, and the right one.
/// </para>
/// </summary>
public static class EdgeRemediation
{
    /// <summary>What the app should do about a theme's interactive edge on this launch.</summary>
    public enum Decision
    {
        /// <summary>The theme's own boundary already clears 3:1 — nothing to derive, nothing to ask.</summary>
        LeaveAlone,

        /// <summary>Derive without asking. Built-in themes only: the defect is ours to fix.</summary>
        DeriveSilently,

        /// <summary>A user theme falls short and its author has not been asked yet.</summary>
        AskFirst,

        /// <summary>Asked and declined. Their theme, their call — render it as authored.</summary>
        HonourDecline,
    }

    /// <param name="isBuiltIn">Built-ins are ours; user themes belong to whoever wrote them.</param>
    /// <param name="alreadyAnswered">
    /// Whether this specific theme id has been answered before. Tracked per theme, not per app, so
    /// switching themes can ask again about a different theme while the same one never asks twice.
    /// </param>
    /// <param name="declined">The answer, when there was one.</param>
    public static Decision Decide(bool isBuiltIn, string? navy, string? divider, bool alreadyAnswered, bool declined)
    {
        var ratio = ContrastGuard.RatioBetween(navy, divider);

        // Unparseable values are not a contrast problem and are not a question worth asking; the
        // guard already returns such input unchanged.
        if (ratio is null || ratio >= ContrastGuard.MinimumBoundaryRatio) return Decision.LeaveAlone;

        // Asking permission to fix our own defect is theatre, and it would fire for every user on
        // the default theme — a dialog on first launch for something they did not author.
        if (isBuiltIn) return Decision.DeriveSilently;

        if (!alreadyAnswered) return Decision.AskFirst;

        return declined ? Decision.HonourDecline : Decision.DeriveSilently;
    }

    /// <summary>
    /// The boundary colour to render for this decision. <see cref="Decision.AskFirst"/> renders the
    /// derived edge while the dialog is up — the question is whether to KEEP it, so showing the
    /// change is what makes the question answerable.
    /// </summary>
    public static string Resolve(Decision decision, string? navy, string? divider) => decision switch
    {
        Decision.HonourDecline => divider ?? "",
        Decision.LeaveAlone => divider ?? "",
        _ => ContrastGuard.Ensure(navy, divider),
    };
}
