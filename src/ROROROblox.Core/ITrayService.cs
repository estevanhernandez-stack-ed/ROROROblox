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

    /// <summary>Fired when the user picks "Open ROROROblox" from the tray menu (or left-clicks).</summary>
    event EventHandler RequestOpenMainWindow;

    /// <summary>Fired when the user toggles the "Multi-Instance" menu item.</summary>
    event EventHandler RequestToggleMutex;

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
}
