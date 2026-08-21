using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ROROROblox.App.History;

/// <summary>
/// A history row that exists in the automation tree (F-072).
/// <para>
/// WHY A SUBCLASS AND NOT A PROPERTY. The row is a <see cref="Border"/>, and WPF builds automation
/// peers for CONTROLS — a decorator is not one, so <c>AutomationProperties.SetName</c> on it creates
/// no node and the name goes nowhere. That is not a guess: with the name set on the Border the
/// History window still reported 510 Text nodes and <b>zero</b> named rows. Swapping the Border for
/// a <see cref="ContentControl"/> changed nothing either — a plain ContentControl gets no peer of
/// its own, and the tree showed exactly one Custom element for a hundred rows.
/// </para>
/// <para>
/// So the container has to be something that answers <see cref="UIElement.OnCreateAutomationPeer"/>.
/// Subclassing is the smallest thing that does, and it changes no pixels: a
/// <c>ListBoxItem</c> would have brought a selection template and mouse-over chrome with it, which
/// is a visual change smuggled in behind an accessibility fix.
/// </para>
/// <para>
/// The repo has been here before. Wave 3's Tools control took a custom peer after all three stock
/// controls each failed a different half of what it needed, and the reasoning is the same: the
/// automation tree is a real output, and WPF only populates it for things it considers controls.
/// </para>
/// </summary>
internal sealed class HistoryRowPresenter : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new HistoryRowPeer(this);

    /// <summary>
    /// Reports the row as a list item carrying one composed sentence.
    /// <para>
    /// <see cref="ControlType.ListItem"/> rather than <c>Custom</c> because that is what it is, and
    /// because F-072's evidence counted "zero DataItem/List containers" as part of the defect. This
    /// does not make History a real list — there is no selection, no ItemsControl, and arrow keys do
    /// nothing — and the row says plainly that the rebuild is not its scope. What it does is stop a
    /// hundred rows being announced as five hundred loose fragments.
    /// </para>
    /// </summary>
    private sealed class HistoryRowPeer(HistoryRowPresenter owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        /// <summary>
        /// The whole point: without this the peer exists and the tree still skips it, because WPF
        /// omits elements that do not consider themselves controls from the control view.
        /// </summary>
        protected override bool IsControlElementCore() => true;

        protected override string GetClassNameCore() => nameof(HistoryRowPresenter);
    }
}
