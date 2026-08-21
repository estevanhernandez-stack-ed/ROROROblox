namespace ROROROblox.Core;

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
/// </summary>
public static class MultiInstanceStatusLine
{
    /// <summary>The tray icon's hover text. Carries the product name because nothing else near it does.</summary>
    public static string Tooltip(MultiInstanceState state) => state switch
    {
        MultiInstanceState.On => $"{Branding.ProductName} — Multi-Instance ON",
        MultiInstanceState.Off => $"{Branding.ProductName} — Multi-Instance OFF",
        MultiInstanceState.Error => $"{Branding.ProductName} — Multi-Instance ERROR (mutex lost)",
        _ => Branding.ProductName,
    };

    /// <summary>
    /// The tray menu item. It is also the button that toggles the state, so the ERROR arm says what
    /// clicking will do — on MutexLost the handle is already released, and the same click re-acquires.
    /// </summary>
    public static string MenuHeader(MultiInstanceState state) => state switch
    {
        MultiInstanceState.On => "Multi-Instance: ON ✓",
        MultiInstanceState.Error => "Multi-Instance: ERROR — click to reload",
        _ => "Multi-Instance: OFF",
    };

    /// <summary>The main window footer. State only — the switch stays in the tray (F-018).</summary>
    public static string StatusBar(MultiInstanceState state) => state switch
    {
        MultiInstanceState.On => "Multi-Instance on",
        MultiInstanceState.Error => "Multi-Instance error",
        _ => "Multi-Instance off",
    };

    /// <summary>
    /// What the footer line cannot fit: what the state means for launching, and where to change it.
    /// Every arm names the tray, because a user who has never right-clicked the icon has no way to
    /// discover that the switch exists at all.
    /// </summary>
    public static string StatusBarTooltip(MultiInstanceState state) => state switch
    {
        MultiInstanceState.On =>
            "Roblox clients can run side by side. Toggle this from the tray icon's right-click menu.",
        MultiInstanceState.Error =>
            "The Roblox singleton lock was lost, so new clients may refuse to open. Right-click the "
            + "tray icon and click Multi-Instance to take it back.",
        _ =>
            "Only one Roblox client can run at a time. Right-click the tray icon and click "
            + "Multi-Instance to turn it on.",
    };

    /// <summary>
    /// Whether the state is the one the product exists to provide. Drives emphasis only — the footer
    /// dot beside it takes the same live/quiet pair the client-count dot does, so this returns a
    /// plain bool rather than anything that names a colour (invariant 1).
    /// </summary>
    public static bool IsHealthy(MultiInstanceState state) => state == MultiInstanceState.On;
}
