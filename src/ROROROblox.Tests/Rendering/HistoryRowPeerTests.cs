using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ROROROblox.Tests.Rendering;

/// <summary>
/// F-072 — the row reaches the automation tree at all.
/// <para>
/// This exists because the obvious implementations do not work and fail SILENTLY. Setting
/// <c>AutomationProperties.Name</c> on the row's <see cref="System.Windows.Controls.Border"/>
/// produced nothing: the History window still reported 510 Text nodes and zero named rows, because
/// WPF builds peers for controls and a decorator is not one. Swapping in a
/// <see cref="ContentControl"/> produced nothing either — the tree showed a single Custom element
/// for a hundred rows. Both diffs read as correct. Only the automation tree said otherwise.
/// </para>
/// <para>
/// So the two properties asserted here are the entire fix, and each of them was false at some point
/// during it: the peer must EXIST, and it must claim to be a control element, because WPF omits
/// elements that do not from the control view no matter what name they carry.
/// </para>
/// </summary>
public class HistoryRowPeerTests
{
    [WindowRenderFact]
    public void TheRowPresenterReachesTheControlViewAsANamedListItem()
    {
        var (type, isControl, name) = WindowRenderHost.Run(() =>
        {
            var row = new App.History.HistoryRowPresenter();
            AutomationProperties.SetName(row, "estehernandez, Pet Sim, started 4:57 PM, 1 min.");

            var peer = UIElementAutomationPeer.CreatePeerForElement(row);
            Assert.NotNull(peer);

            return (peer!.GetAutomationControlType(), peer.IsControlElement(), peer.GetName());
        }, "history row peer");

        // ListItem rather than Custom: F-072's evidence counted "zero DataItem/List containers" as
        // part of the defect, and this is what the row actually is.
        Assert.Equal(AutomationControlType.ListItem, type);

        // The one that was false twice. A peer that does not claim to be a control element is
        // skipped by the control view entirely, name and all — which is exactly how a hundred rows
        // came to be invisible while carrying perfectly good names.
        Assert.True(isControl, "The row peer must claim to be a control element or the tree skips it.");

        Assert.Equal("estehernandez, Pet Sim, started 4:57 PM, 1 min.", name);
    }

    [WindowRenderFact]
    public void APlainBorderStillHasNoPeer_WhichIsWhyThePresenterExists()
    {
        // Pins the reason rather than the fix. If a future WPF makes decorators peer-bearing, this
        // fails and someone gets to delete the subclass with evidence instead of a hunch.
        var peer = WindowRenderHost.Run(() =>
        {
            var border = new Border();
            AutomationProperties.SetName(border, "named, but nowhere.");
            return UIElementAutomationPeer.CreatePeerForElement(border);
        }, "bare border peer");

        Assert.Null(peer);
    }
}
