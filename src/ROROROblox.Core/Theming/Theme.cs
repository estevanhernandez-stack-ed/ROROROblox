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
}
