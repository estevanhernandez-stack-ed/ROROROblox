namespace ROROROblox.App.AppLifecycle;

/// <summary>Where a dialog should sit, and whether it gets a parent.</summary>
public enum DialogPlacement
{
    /// <summary>Parent it to the main window and centre on it, the ordinary case.</summary>
    OwnedByMainWindow = 0,

    /// <summary>No usable parent — stand alone, centred on the screen.</summary>
    CenteredOnScreen,
}

/// <summary>
/// Decides whether the main window can actually own a dialog (F-084).
/// <para>
/// WHY THIS EXISTS. Every dialog site said <c>Owner = Application.Current.MainWindow</c>
/// unconditionally. RoRoRo is tray-resident, so the main window is frequently hidden — and a dialog
/// raised from the tray then becomes the child of a window nobody can see. It inherits that
/// parent's z-order and, on dismissal, hands activation back to something invisible instead of
/// falling through to whatever the user was actually looking at. <c>StopAllConfirmWindow.xaml</c>
/// documents the intended behaviour — "CenterScreen (no owner) — the tray path has no visible
/// parent" — which is the correct rule, written down in one window and applied in none.
/// </para>
/// <para>
/// THE PLACEMENT HALF MATTERS TOO, and is the part that makes this more than a one-word guard.
/// Twenty-one windows declare <c>WindowStartupLocation="CenterOwner"</c>. With no owner, WPF does
/// not fall back to the screen centre — <c>CenterOwner</c> degrades to <c>Manual</c>, which puts
/// the dialog in the top-left corner. So dropping the owner without also fixing the placement
/// trades an invisible parent for a dialog in the corner of the display.
/// </para>
/// <para>
/// Pure and unit-tested for the same reason <see cref="BlockedStartupDecision"/> and
/// <see cref="LeftoverStartupDecision"/> are: the rule is the part worth pinning, and the code that
/// applies it constructs real WPF windows, which is the untestable orchestration F-106 names.
/// </para>
/// </summary>
public static class DialogOwnershipDecision
{
    /// <param name="ownerExists">Whether there is a main window at all.</param>
    /// <param name="ownerIsVisible">Whether it is actually on screen — the check that was missing.</param>
    public static DialogPlacement Decide(bool ownerExists, bool ownerIsVisible) =>
        ownerExists && ownerIsVisible
            ? DialogPlacement.OwnedByMainWindow
            : DialogPlacement.CenteredOnScreen;
}
