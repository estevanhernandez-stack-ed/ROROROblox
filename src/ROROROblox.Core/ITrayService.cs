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
}
