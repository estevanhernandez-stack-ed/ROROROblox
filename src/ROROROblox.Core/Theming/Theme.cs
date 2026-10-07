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

    // ========================= THE LIBRARY'S ACCENT, TAKEN OVER =========================
    //
    // The four below are the only keys in this class whose NAME is not ours. They are WPF-UI's,
    // and they are here because WPF-UI's CheckBox template resolves every one of its state fills
    // through {DynamicResource} against its own Dark dictionary, where the checked fill is the
    // fixed Fluent blue #0067C0. That blue is not in any theme and no theme change touches it, so
    // every checkbox in the app has painted library blue when ticked for the app's whole life.
    //
    // It is most wrong under flatline, a theme whose entire premise is carrying no meaning in
    // colour, where a ticked box was the one blue object on the screen. It became impossible to
    // ignore when the v1.33 Alerts page put 28 of them on one page.
    //
    // WHY OVERRIDE THE KEYS AND NOT OWN THE TEMPLATE, which is what F-068 did for buttons. That
    // decision turned on the inherited Button template's state triggers being hardcoded LITERALS
    // addressed to an element named "border" — unreachable from a Style, so there was nothing to
    // override and the template had to be replaced. WPF-UI's CheckBox is the opposite case: every
    // state fill, the check glyph and both hover fills are already {DynamicResource} lookups, so
    // they resolve against whatever dictionary wins. Replacing that template would cost the check
    // animation and 100 lines of borrowed markup to reach a result four keys reach, and F-068's
    // own note on the cost of owning a template is the argument against doing it when it is
    // avoidable.
    //
    // WHY THEY ARE WRITTEN FROM C# AND NOT DECLARED IN ControlStyles.xaml, which merges after
    // WPF-UI's dictionaries and is where an override belongs: XAML cannot alias one brush resource
    // to another. `Color="{DynamicResource CyanBrush}"` wants a Color and the theme supplies a
    // Brush, and `{StaticResource}` would freeze the pre-startup instance that ApplySlot throws
    // away on the first apply — the exact failure App.xaml's brush comment warns about. The
    // pre-startup values are declared in App.xaml beside the ten slots, and ThemeService.ApplySlot
    // REPLACES them on every theme change like every other brush the theme owns. An entry written
    // directly into Application.Current.Resources outranks every merged dictionary, WPF-UI's
    // included, so the override is strictly stronger than one in ControlStyles.xaml would be.

    /// <summary>
    /// WPF-UI's checked-checkbox fill, bound to the theme's own accent (<see cref="Cyan"/>).
    /// <para>
    /// Not derived in the arithmetic sense — it IS the accent slot, under a second key, because the
    /// library's template is what reads it. Flatline needs no special case: its accent is
    /// <c>#D4D4D4</c>, so a ticked box there goes light grey and the theme keeps its promise.
    /// </para>
    /// </summary>
    public const string CheckedAccent = "CheckBoxCheckBackgroundFillChecked";

    /// <summary>
    /// The checked fill while the pointer is over the box. <b>WPF-UI ships this as Transparent</b>
    /// (<c>#00FFFFFF</c>) in its Dark dictionary, and its template assigns it to the OUTER border's
    /// Background — so hovering a ticked box erased the tick's fill entirely and left a glyph
    /// floating on the row behind it. That was shipped, in every theme, and is a second defect the
    /// accent fix had to carry rather than inherit.
    /// <para>
    /// Derived as the accent with the button vocabulary's own hover sheen composited onto it, so a
    /// hovered checkbox and a hovered CTA lighten by the same amount. It is a PRE-COMPOSITED sheen
    /// rather than a layer because the library's trigger replaces a fill and we do not own that
    /// template; the pixel is identical, and what the sheen rule actually forbids — replacing a
    /// bright fill with a SURFACE colour, which is what made a hovered cyan button go navy at C1 —
    /// cannot happen here, because the replacement is the accent plus white.
    /// </para>
    /// </summary>
    public const string CheckedAccentHover = "CheckBoxCheckBackgroundFillCheckedPointerOver";

    /// <summary>
    /// The checked fill while the box is held down. Transparent in WPF-UI's Dark dictionary for the
    /// same reason <see cref="CheckedAccentHover"/> is, and derived the same way at the press
    /// sheen's heavier opacity, so hover and pressed stay distinguishable.
    /// </summary>
    public const string CheckedAccentPressed = "CheckBoxCheckBackgroundFillCheckedPressed";

    /// <summary>
    /// The check glyph itself, derived against <see cref="CheckedAccent"/> exactly the way
    /// <see cref="OnMagenta"/> is derived against <see cref="Magenta"/>: pick the more readable of
    /// the theme's own two text colours, then nudge only if the better one still falls short.
    /// <para>
    /// WPF-UI ships pure black. That is legible on its blue and says nothing about ours — and
    /// hardcoding <see cref="Navy"/> instead, which is what <c>CtaButtonStyle</c> does for the same
    /// cyan fill, is right for all four built-ins and wrong the moment somebody authors a theme
    /// with a dark accent. Picking costs nothing, because both candidates are the author's.
    /// </para>
    /// <para>
    /// Derived against the RESTING fill. The hover and pressed fills are the resting one washed
    /// toward White, so a dark glyph only gains contrast as the pointer arrives; a theme dark enough
    /// for the glyph to resolve White would lose some, and all three pairs are measured per theme by
    /// <c>ContrastPairGateTests.NamedPairs</c> rather than argued about here.
    /// </para>
    /// </summary>
    public const string OnCheckedAccent = "CheckBoxCheckGlyphForeground";
}
