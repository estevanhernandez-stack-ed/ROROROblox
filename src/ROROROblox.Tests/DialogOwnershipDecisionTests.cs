using ROROROblox.App.AppLifecycle;

namespace ROROROblox.Tests;

/// <summary>
/// F-084. A tray-invoked dialog was parented to the main window whether or not anyone could see it.
/// </summary>
public class DialogOwnershipDecisionTests
{
    [Fact]
    public void AVisibleMainWindowOwnsItsDialogs()
    {
        Assert.Equal(DialogPlacement.OwnedByMainWindow, DialogOwnershipDecision.Decide(ownerExists: true, ownerIsVisible: true));
    }

    [Fact]
    public void TheShippedDefect_AHiddenMainWindowMustNotOwnAnything()
    {
        // RoRoRo lives in the tray, so this is the ordinary state, not an edge case. Owned by a
        // hidden window, a dialog inherits an invisible z-order and hands activation back to
        // nothing when dismissed.
        Assert.Equal(DialogPlacement.CenteredOnScreen, DialogOwnershipDecision.Decide(ownerExists: true, ownerIsVisible: false));
    }

    [Fact]
    public void NoMainWindowAtAllCentresOnScreen()
    {
        // Startup dialogs run before MainWindow exists — the gate's own modals are shown from
        // OnStartup, long before anything is shown.
        Assert.Equal(DialogPlacement.CenteredOnScreen, DialogOwnershipDecision.Decide(ownerExists: false, ownerIsVisible: false));
    }

    [Fact]
    public void AnInvisibleWindowIsNeverUsableEvenIfItSomehowReportsExisting()
    {
        // Guards the ordering of the condition: existence alone must not be enough, which is
        // precisely the bug — every site tested existence implicitly and visibility never.
        Assert.NotEqual(DialogPlacement.OwnedByMainWindow, DialogOwnershipDecision.Decide(true, false));
    }
}
