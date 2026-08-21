namespace ROROROblox.Core.Theming;

/// <summary>
/// One color theme — a flat dictionary of brand-slot hex values. Sanduhr-style: drop a JSON
/// file in <c>%LOCALAPPDATA%\ROROROblox\themes\</c> and it appears in the picker. The slot
/// names mirror the brushes referenced from XAML so a theme swap is a one-pass dictionary
/// update with no per-window plumbing.
/// </summary>
public sealed record Theme(
    string Id,
    string Name,
    string Bg,
    string Cyan,
    string Magenta,
    string White,
    string MutedText,
    string Divider,
    string RowBg,
    string RowExpiredBg,
    string RowExpiredAccent,
    string Navy,
    bool IsBuiltIn = false);

/// <summary>
/// The slot names in the same order they appear in <see cref="Theme"/>. Used by the JSON loader
/// to validate user-supplied themes and by the theme service to know which keys to overwrite
/// in <c>Application.Current.Resources</c>. Centralizing here keeps the XAML keys + JSON field
/// names + record properties in lockstep.
/// </summary>
public static class ThemeSlots
{
    public const string Bg = "BgBrush";
    public const string Cyan = "CyanBrush";
    public const string Magenta = "MagentaBrush";
    public const string White = "WhiteBrush";
    public const string MutedText = "MutedTextBrush";
    public const string Divider = "DividerBrush";
    public const string RowBg = "RowBgBrush";
    public const string RowExpiredBg = "RowExpiredBgBrush";
    public const string RowExpiredAccent = "RowExpiredAccentBrush";
    public const string Navy = "NavyBrush";

    /// <summary>
    /// DERIVED, not a theme slot — no JSON field, nothing for a theme author to supply, and it is
    /// deliberately absent from <see cref="Theme"/>. Computed by <see cref="ContrastGuard"/> from
    /// Divider against EVERY surface a control lands on, so an interactive control's edge always
    /// clears WCAG 1.4.11's 3:1, whatever a theme sets.
    /// <para>
    /// "Every surface" is F-090 and is not a detail. This was derived against <c>Navy</c> alone
    /// until 2026-08-20, where it cleared 3:1 by the width of the derivation walk — and every
    /// built-in ships <c>RowBg</c> lighter than Navy, so the same edge on a card measured 2.82:1 in
    /// brand, midnight and magenta-heat and 2.28:1 in flatline. Eight of the fourteen call sites
    /// for the input styles sit on a card. The arithmetic was never wrong; it was answering a
    /// question about one surface while being painted on two.
    /// </para>
    /// <para>
    /// USE THIS ONLY ON INTERACTIVE CONTROLS. `Divider` does two jobs: a decorative separator
    /// between rows and around cards, where the author's faint hairline is correct and intended,
    /// and the boundary of a control, where 3:1 is required. 1.4.11 governs component boundaries,
    /// not separators. Binding this brush to a card edge or a row rule would repaint every user's
    /// theme from a hairline to mid grey — #1F3149 -> #707B8B in brand — to fix a problem those
    /// surfaces do not have. A test enforces the split; see the wave-5 scope.
    /// </para>
    /// </summary>
    public const string InteractiveEdge = "InteractiveEdgeBrush";

    /// <summary>
    /// DERIVED, like <see cref="InteractiveEdge"/> — the label colour for anything filled with
    /// <see cref="Magenta"/> (F-050).
    /// <para>
    /// Magenta is the one fill in the palette where neither of the theme's two text colours is
    /// reliably readable. Measured 2026-08-20 against WCAG 1.4.3 AA's 4.5:1: white reaches 3.79:1
    /// on brand and 3.29:1 on magenta-heat, navy reaches 3.79:1 on midnight and 3.73:1 on flatline
    /// — so each is the wrong answer in half the built-ins, and the BETTER of the two still falls
    /// short in brand (4.40:1) and midnight (4.16:1). Picking per theme is most of the fix;
    /// <see cref="ContrastGuard.BestForeground"/> picks, then nudges the winner the rest of the way.
    /// </para>
    /// <para>
    /// USE THIS ONLY ON A MAGENTA FILL. It is derived against Magenta and says nothing about any
    /// other surface — the same rule, and the same reason, as the interactive edge above.
    /// </para>
    /// <para>
    /// NOT ON THE PLUGIN WIRE. <c>ThemePalette</c> in the contract proto carries eleven fields and
    /// this is a twelfth resolved brush; adding it is additive and backwards-compatible, but it is a
    /// versioned contract change with an author guide behind it, and that is a decision of its own
    /// rather than a side effect of a contrast fix. Recorded as a gap, not solved here.
    /// </para>
    /// </summary>
    public const string OnMagenta = "OnMagentaBrush";
}
