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

    /// <summary>Show a passive, non-blocking notification (tray balloon). Used for idle warnings.</summary>
    void ShowToast(string title, string message);

    /// <summary>
    /// Memory-pressure warning overlay (Task 8). Deliberately SEPARATE from <see cref="UpdateStatus"/> —
    /// <see cref="MultiInstanceState"/> answers "is multi-instance working", an unrelated axis.
    /// Folding memory pressure into it would erase the ON/ERROR state the user needs during a
    /// real mutex problem, which is the more urgent failure. A mutex ERROR icon always wins the
    /// tray slot; the warning badge only paints over the ON/OFF icons.
    /// </summary>
    void SetMemoryWarning(bool active);

    /// <summary>
    /// Balloon for a newly-crossed memory threshold. Fires once per latched crossing.
    /// <para>
    /// Deviation from the task brief's literal <c>(string title, string message)</c> signature:
    /// a Windows balloon-click event carries no payload of its own, so <paramref name="accountId"/>
    /// is remembered here and replayed on <see cref="RequestFocusAccount"/> when the user clicks —
    /// there is no other channel for the click handler to learn which account the balloon was about.
    /// </para>
    /// </summary>
    void ShowMemoryWarning(string title, string message, Guid accountId);

    /// <summary>Fired when the user clicks a memory-warning balloon — carries the target account.</summary>
    event EventHandler<Guid> RequestFocusAccount;
}
