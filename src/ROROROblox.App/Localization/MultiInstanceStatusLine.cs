using ROROROblox.Core;

namespace ROROROblox.App.Localization;

/// <summary>
/// Every sentence the app says about <see cref="MultiInstanceState"/>, in one place (F-018, F-034).
/// <para>
/// WHY IT IS CENTRAL. Multi-Instance is the product's core switch and it was reported in eight
/// places — three tray tooltips, three tray menu headers, a Diagnostics row and a support-bundle
/// line — with the phrasing copied by hand into each. Two consequences, both already paid for:
/// the tooltip carried the repo name in all three states while the menu beside it did not (F-034),
/// and F-071's proposal to rename the concept for a non-technical audience prices out as an
/// eight-site edit, which is why it has stayed open at severity 1 since the audit. Routing the
/// wording through one type does not make that rename correct — it is a product decision nobody
/// has taken — but it makes it a decision rather than a sweep.
/// </para>
/// <para>
/// The status-bar line is deliberately the shortest of the three. It sits in a footer beside a live
/// client count and a compact toggle, and a footer that explains itself stops being chrome. What it
/// cannot fit goes in <see cref="StatusBarTooltip"/>, which is also the only place that says where
/// the switch actually lives — the tray menu Windows hides behind an overflow chevron by default.
/// </para>
/// <para>
/// WHY IT LIVES IN THE APP (Core string boundary, localization Phase D, 2026-09-07). Every arm here
/// is text a viewer reads, and its only callers are the App — the status-bar VM and the tray. It was
/// raw English prose in Core; it composes each arm from resx now (<see cref="Loc"/>), so a live
/// culture toggle re-narrates the footer and tray alike. "Multi-Instance" stays verbatim across
/// languages — it is a product noun (the PRODUCT_NOUNS guard keeps it English in every catalog).
/// </para>
/// </summary>
public static class MultiInstanceStatusLine
{
    /// <summary>The tray icon's hover text. Carries the product name because nothing else near it does.</summary>
    public static string Tooltip(MultiInstanceState state) => state switch
    {
        MultiInstanceState.On => Loc.Format("MultiInstance_Tooltip_On", Branding.ProductName),
        MultiInstanceState.Off => Loc.Format("MultiInstance_Tooltip_Off", Branding.ProductName),
        MultiInstanceState.Error => Loc.Format("MultiInstance_Tooltip_Error", Branding.ProductName),
        _ => Branding.ProductName,
    };

    /// <summary>
    /// The tray menu item. It is also the button that toggles the state, so the ERROR arm says what
    /// clicking will do — on MutexLost the handle is already released, and the same click re-acquires.
    /// </summary>
    public static string MenuHeader(MultiInstanceState state) => state switch
    {
        MultiInstanceState.On => Loc.Get("MultiInstance_MenuHeader_On"),
        MultiInstanceState.Error => Loc.Get("MultiInstance_MenuHeader_Error"),
        _ => Loc.Get("MultiInstance_MenuHeader_Off"),
    };

    /// <summary>The main window footer. State only — the switch stays in the tray (F-018).</summary>
    public static string StatusBar(MultiInstanceState state) => state switch
    {
        MultiInstanceState.On => Loc.Get("MultiInstance_StatusBar_On"),
        MultiInstanceState.Error => Loc.Get("MultiInstance_StatusBar_Error"),
        _ => Loc.Get("MultiInstance_StatusBar_Off"),
    };

    /// <summary>
    /// What the footer line cannot fit: what the state means for launching, and where to change it.
    /// Every arm names the tray, because a user who has never right-clicked the icon has no way to
    /// discover that the switch exists at all.
    /// </summary>
    public static string StatusBarTooltip(MultiInstanceState state) => state switch
    {
        MultiInstanceState.On => Loc.Get("MultiInstance_StatusBarTooltip_On"),
        MultiInstanceState.Error => Loc.Get("MultiInstance_StatusBarTooltip_Error"),
        _ => Loc.Get("MultiInstance_StatusBarTooltip_Off"),
    };

    /// <summary>
    /// Whether the state is the one the product exists to provide. Drives emphasis only — the footer
    /// dot beside it takes the same live/quiet pair the client-count dot does, so this returns a
    /// plain bool rather than anything that names a colour (invariant 1).
    /// </summary>
    public static bool IsHealthy(MultiInstanceState state) => state == MultiInstanceState.On;
}
