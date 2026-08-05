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
    /// Navy and Divider so an interactive control's edge always clears WCAG 1.4.11's 3:1, whatever
    /// a theme sets.
    /// <para>
    /// USE THIS ONLY ON INTERACTIVE CONTROLS. `Divider` does two jobs: a decorative separator
    /// between rows and around cards, where the author's faint hairline is correct and intended,
    /// and the boundary of a control, where 3:1 is required. 1.4.11 governs component boundaries,
    /// not separators. Binding this brush to a card edge or a row rule would repaint every user's
    /// theme from a hairline to mid grey — measured at #1F3149 -> #5E6B7C in brand — to fix a
    /// problem those surfaces do not have. A test enforces the split; see the wave-5 scope.
    /// </para>
    /// </summary>
    public const string InteractiveEdge = "InteractiveEdgeBrush";
}
