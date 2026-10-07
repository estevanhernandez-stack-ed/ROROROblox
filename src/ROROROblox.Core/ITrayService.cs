namespace ROROROblox.Core;

/// <summary>
/// Owns the system tray icon + context menu. Spec §5.2. Doesn't own the mutex itself —
/// requests toggle via <see cref="RequestToggleMutex"/>; the composition root wires that
/// to <see cref="IMutexHolder.Acquire"/> / <see cref="IMutexHolder.Release"/>.
/// </summary>
public interface ITrayService : IDisposable
{
    void Show();
    void UpdateStatus(MultiInstanceState state);

    /// <summary>
    /// Raised by <see cref="UpdateStatus"/> whenever the multi-instance state is set — the one
    /// funnel every caller already goes through (F-018).
    /// <para>
    /// The main window's footer reports this state, and this is how it hears about it. Six places
    /// in the composition root call <see cref="UpdateStatus"/>; notifying a second surface from
    /// each of them would be six chances to forget one, and the state that would go stale is the
    /// ERROR arm — the transition that only happens when something has already gone wrong.
    /// </para>
    /// <para>
    /// Fires on every call, not only on a change, and carries no thread guarantee: at least one
    /// caller raises it off the UI thread (<c>MutexLost</c>). Subscribers that touch bound state
    /// marshal it themselves.
    /// </para>
    /// </summary>
    event EventHandler<MultiInstanceState> StatusChanged;

    /// <summary>Fired when the user picks "Open RoRoRo" from the tray menu (or left-clicks).</summary>
    event EventHandler RequestOpenMainWindow;

    /// <summary>Fired when the user toggles the "Multi-Instance" menu item.</summary>
    event EventHandler RequestToggleMutex;

    /// <summary>Fired when the user picks "Stop all Roblox instances" from the tray menu.</summary>
    event EventHandler RequestStopAllInstances;

    /// <summary>Fired when the user picks "Quit" from the tray menu.</summary>
    event EventHandler RequestQuit;

    /// <summary>Fired when the user picks "Diagnostics..." from the tray menu.</summary>
    event EventHandler RequestOpenDiagnostics;

    /// <summary>Fired when the user picks "Open log folder" from the tray menu.</summary>
    event EventHandler RequestOpenLogs;

    /// <summary>Fired when the user picks "Preferences..." from the tray menu.</summary>
    event EventHandler RequestOpenPreferences;

    /// <summary>Fired when the user picks "History..." from the tray menu.</summary>
    event EventHandler RequestOpenHistory;

    /// <summary>Fired when the user picks "Plugins..." from the tray menu.</summary>
    event EventHandler RequestOpenPlugins;

    /// <summary>
    /// Fired when the user double-clicks the tray icon. The composition root decides whether
    /// to launch the main account (if eligible) or fall back to surfacing the main window.
    /// </summary>
    event EventHandler RequestActivateMain;

    /// <summary>
    /// Show a passive, non-blocking notification. THE one way anything reaches a user's screen as
    /// an interruption — every alert kind routed to
    /// <see cref="ROROROblox.Core.Discord.AlertDestination.Local"/> arrives here (v1.33 item 3).
    /// <para>
    /// <paramref name="accountId"/> is the single account the notification is ABOUT, and it is what
    /// a click on the notification replays through <see cref="RequestFocusAccount"/>. Trailing and
    /// optional because most callers have nothing to say here and a Windows balloon click carries no
    /// payload of its own, so the account has to arrive with the notification or not at all.
    /// </para>
    /// <para>
    /// <b>Null means "about more than one account, or about none."</b> A coalesced group of three
    /// drops has no single row to jump to, and the periodic uptime mark is about the machine rather
    /// than an account (its carrier id is <see cref="Guid.Empty"/>). Such a notification is honestly
    /// unclickable rather than clickable-and-arbitrary. The caller that decides this is
    /// <c>AlertDispatcher</c>, once per routed group.
    /// </para>
    /// <para>
    /// Until v1.33 item 4 this was <c>ShowToast(title, message)</c> plus a separate
    /// <c>ShowMemoryWarning(title, message, accountId)</c>, and only the memory warning's click went
    /// anywhere. Collapsing the two generalised click-to-focus to every single-account kind — a
    /// drop, a recycle, an idle crossing, a metric breach — instead of restoring it as a
    /// memory-warning privilege.
    /// </para>
    /// </summary>
    void ShowToast(string title, string message, Guid? accountId = null);

    /// <summary>
    /// Memory-pressure warning overlay (Task 8). Deliberately SEPARATE from <see cref="UpdateStatus"/> —
    /// <see cref="MultiInstanceState"/> answers "is multi-instance working", an unrelated axis.
    /// Folding memory pressure into it would erase the ON/ERROR state the user needs during a
    /// real mutex problem, which is the more urgent failure. A mutex ERROR icon always wins the
    /// tray slot; the warning badge only paints over the ON/OFF icons.
    /// </summary>
    void SetMemoryWarning(bool active);

    /// <summary>Fired when the user clicks a notification that named one account — carries it.</summary>
    event EventHandler<Guid> RequestFocusAccount;
}
