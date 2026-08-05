using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace ROROROblox.App.Controls;

/// <summary>
/// The main window's "Tools" menu button: a Button that owns a ContextMenu, and tells assistive
/// technology the truth about it.
/// <para>
/// Glow wave 3 collapsed six main-window nav buttons into this one control. Six buttons were six
/// announced, discoverable destinations. A plain Button-with-ContextMenu announces as a button with
/// nothing behind it — no ExpandCollapse pattern, no expanded/collapsed state — so a screen-reader
/// user is told there is a button called Tools and nothing about the five destinations. That is a
/// regression the sighted redesign does not pay for, so this class supplies the missing pattern.
/// </para>
/// <para>
/// WHY NOT A STOCK CONTROL. All three were tried against the running app and each failed a
/// different half:
/// </para>
/// <list type="bullet">
/// <item>WPF <c>Menu</c> + top-level <c>MenuItem</c> reports ExpandCollapse and highlights its first
/// item on keyboard open — but its submenu popup falls back to WPF's classic light chrome, because
/// WPF-UI styles <c>ContextMenu</c> and not <c>Menu</c>. A white popup on a navy app, and worse
/// under a user theme.</item>
/// <item>WPF-UI's <c>DropDownButton</c> with a <c>ContextMenu</c> in its <c>Flyout</c> did not open
/// at all: clicking it left <c>IsDropDownOpen</c> false and produced no menu items.</item>
/// <item>A plain <c>Button</c> with a <c>ContextMenu</c> opens correctly and themes correctly —
/// WPF-UI styles ContextMenu, and it was verified legible under the adversarial flatline theme —
/// but exposes only Invoke.</item>
/// </list>
/// <para>
/// So: keep the one that renders right, and fix what it does not say. Opening, the second-click
/// dismiss, and keyboard focus all live here rather than in a window code-behind handler, so the
/// behaviour travels with the control instead of being re-derived at each call site.
/// </para>
/// </summary>
internal sealed class ToolsDropDownButton : Button
{
    private bool _hooked;

    protected override AutomationPeer OnCreateAutomationPeer() => new ToolsDropDownButtonAutomationPeer(this);

    internal ContextMenu? Menu => ContextMenu;

    /// <summary>
    /// Click — and, because Space and Enter on a focused Button route through Click, the keyboard
    /// path too. A second activation closes the menu instead of re-opening it: WPF dismisses the
    /// popup on the click that lands outside it, so an unconditional open would fight that and
    /// flicker or reopen.
    /// </summary>
    protected override void OnClick()
    {
        base.OnClick();

        if (ContextMenu is null) return;

        if (ContextMenu.IsOpen)
        {
            ContextMenu.IsOpen = false;
            return;
        }

        HookMenu();

        // Without an explicit PlacementTarget the menu can take the mouse position instead of the
        // button, which puts it wherever the pointer happened to be on a keyboard activation.
        ContextMenu.PlacementTarget = this;
        ContextMenu.Placement = PlacementMode.Bottom;
        ContextMenu.IsOpen = true;
    }

    private void HookMenu()
    {
        if (_hooked || ContextMenu is null) return;
        _hooked = true;

        ContextMenu.Opened += (_, _) =>
        {
            RaiseStateChanged(wasOpen: false, isOpen: true);

            // Highlight the first item so the menu does not open with nothing focused — the
            // convention every menu button follows, and without it a keyboard user opens the menu
            // and sees no focus indicator until they press Down. Deferred to Input priority
            // because the item containers do not exist yet when Opened fires.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (ContextMenu is null || !ContextMenu.IsOpen) return;
                if (ContextMenu.ItemContainerGenerator.ContainerFromIndex(0) is MenuItem first)
                {
                    first.Focus();
                }
            }));
        };

        ContextMenu.Closed += (_, _) => RaiseStateChanged(wasOpen: true, isOpen: false);
    }

    /// <summary>
    /// Announce open/close as it happens. Without this the pattern reports the right state only
    /// when something asks — a screen reader listening for changes never hears the menu open.
    /// </summary>
    private void RaiseStateChanged(bool wasOpen, bool isOpen)
    {
        if (UIElementAutomationPeer.FromElement(this) is not ToolsDropDownButtonAutomationPeer peer) return;

        peer.RaisePropertyChangedEvent(
            ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
            State(wasOpen),
            State(isOpen));

        static ExpandCollapseState State(bool open) => open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
    }
}

/// <summary>
/// Adds <see cref="IExpandCollapseProvider"/> on top of the button peer, driven by the ContextMenu's
/// own <c>IsOpen</c> so the reported state cannot drift from what is on screen.
/// </summary>
internal sealed class ToolsDropDownButtonAutomationPeer(ToolsDropDownButton owner)
    : ButtonAutomationPeer(owner), IExpandCollapseProvider
{
    private ToolsDropDownButton Button => (ToolsDropDownButton)Owner;

    // What a screen reader reads out as the control's kind. The default is the CLR type name, which
    // is an implementation detail nobody should hear.
    protected override string GetClassNameCore() => "DropDownButton";

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

    public ExpandCollapseState ExpandCollapseState =>
        Button.Menu?.IsOpen == true ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    // Routed through the control's own click path rather than setting ContextMenu.IsOpen directly,
    // so an assistive-technology Expand takes the exact same code as a mouse click — placement,
    // first-item focus and all.
    public void Expand()
    {
        if (Button.Menu is null || Button.Menu.IsOpen) return;
        ((IInvokeProvider)this).Invoke();   // ButtonAutomationPeer implements IInvokeProvider explicitly
    }

    public void Collapse()
    {
        if (Button.Menu is { IsOpen: true } menu) menu.IsOpen = false;
    }
}
