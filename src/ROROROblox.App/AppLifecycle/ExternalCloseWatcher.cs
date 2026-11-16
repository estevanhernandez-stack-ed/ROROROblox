using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ROROROblox.App.AppLifecycle;

/// <summary>
/// Tells a close that arrived from OUTSIDE the process from one made inside it, for one window.
/// <para>
/// WHY. <c>MainWindow.OnClosing</c> cancels every close and hides to the tray, which is right for the X
/// but means a polite close from another process (<c>taskkill</c> without <c>/F</c>, Task Manager's End
/// task) can never end RoRoRo: the window hides, the process stays, and Windows later kills it and files
/// a hang report.
/// </para>
/// <para>
/// HOW. An outside tool delivers <c>WM_CLOSE</c> with <c>PostMessage</c>, so it comes off the thread's
/// message queue. Every in-app close reaches the window as a SENT <c>WM_CLOSE</c>: <c>Window.Close()</c>
/// sends it directly (the WPF-UI title bar's X calls it; read from the 4.3.0 IL and pinned by a test),
/// and Alt+F4, the taskbar's Close and the system menu arrive as <c>WM_SYSCOMMAND/SC_CLOSE</c>, which
/// <c>DefWindowProc</c> turns into a sent <c>WM_CLOSE</c>. WPF raises
/// <see cref="ComponentDispatcher.ThreadFilterMessage"/> for each message its pump takes off the queue and
/// never for a sent one, so a queued <c>WM_CLOSE</c> addressed to this window's handle is a positive
/// identification of an outside close. Anything not positively identified is an ordinary close, which
/// hides: the failure direction is "the window hides", never "the X quits".
/// </para>
/// <para>
/// LIFETIME OF THE NOTE. It is consumed by the first <see cref="TryConsume"/> after it is set, and is
/// also cleared from the dispatcher right after the message that set it has been dispatched, so a
/// <c>WM_CLOSE</c> that never reached <c>OnClosing</c> (swallowed by a hook, say) cannot make the next,
/// unrelated close look external. The filter runs immediately before <c>DispatchMessage</c> for the same
/// message, and <c>OnClosing</c> runs synchronously inside it, so the owning close always sees the note.
/// </para>
/// <para>
/// ACCEPTED LIMIT. A <c>WM_CLOSE</c> posted while a NATIVE modal loop is running (a menu, a move or size
/// drag) is pumped by Windows, not by WPF, so the filter never sees it and the window hides. A WPF modal
/// (<c>ShowDialog</c>) is WPF's own nested pump and is covered.
/// </para>
/// <para>
/// Thread affinity: <see cref="ComponentDispatcher.ThreadFilterMessage"/> is per thread, so this must be
/// created, and disposed, on the window's own thread.
/// </para>
/// </summary>
internal sealed class ExternalCloseWatcher : IDisposable
{
    private const int WM_CLOSE = 0x0010;

    private readonly Window _window;
    private volatile bool _pending;
    private bool _attached;

    public ExternalCloseWatcher(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _window.VerifyAccess();

        ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
        _window.Closed += OnWindowClosed;
        _attached = true;
    }

    /// <summary>True while a queued <c>WM_CLOSE</c> for the window has been seen and not yet consumed.</summary>
    public bool Pending => _pending;

    /// <summary>True until the watcher is disposed or its window has closed.</summary>
    public bool IsAttached => _attached;

    /// <summary>
    /// Returns whether the close being processed was an outside one, and clears the note either way, so
    /// it cannot outlive the close it belongs to. Call it once per close attempt.
    /// </summary>
    public bool TryConsume()
    {
        var was = _pending;
        _pending = false;
        return was;
    }

    private void OnThreadFilterMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != WM_CLOSE)
        {
            return;
        }

        var handle = new WindowInteropHelper(_window).Handle;
        if (handle == IntPtr.Zero || msg.hwnd != handle)
        {
            return;
        }

        _pending = true;

        // Drop the note once this message has been dispatched, in case nothing consumes it.
        _ = _window.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => _pending = false));
    }

    private void OnWindowClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (!_attached)
        {
            return;
        }

        if (!_window.Dispatcher.CheckAccess())
        {
            // Unsubscribing from another thread would silently remove nothing.
            try { _ = _window.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Dispose)); }
            catch { /* the dispatcher is gone, and so is its thread's subscription */ }
            return;
        }

        _attached = false;
        _pending = false;
        ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;
        _window.Closed -= OnWindowClosed;
    }
}
