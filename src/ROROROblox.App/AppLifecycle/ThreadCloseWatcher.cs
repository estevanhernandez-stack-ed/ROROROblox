using System.Windows.Interop;
using System.Windows.Threading;

namespace ROROROblox.App.AppLifecycle;

/// <summary>
/// Sees a polite close from outside the process, whichever of the app's windows it was aimed at.
/// <para>
/// <see cref="ExternalCloseWatcher"/> watches one window, which is enough once the main window is the
/// only thing on screen. It is not enough at startup. <c>taskkill</c> without <c>/F</c> posts
/// <c>WM_CLOSE</c> to ONE window: an unowned top-level one, a visible one if there is any, and of
/// those whichever comes first (measured with a probe and then seen live on 2026-11-17). With the
/// first-run tour up, that window was a toast popup, not the main window and not the tour, so the
/// request closed a toast and the app carried on.
/// </para>
/// <para>
/// The rule is the one v1.39 settled for the main window, applied to the thread: a <c>WM_CLOSE</c>
/// that comes off the message queue was posted, so it came from outside; every close the app makes
/// itself (<c>Window.Close()</c>, the title bar X, Alt+F4, a popup closing) is sent or is not a
/// <c>WM_CLOSE</c> at all, and never reaches <see cref="ComponentDispatcher.ThreadFilterMessage"/>.
/// Same limit as before: a close posted during a native modal loop (a menu, a drag, a file dialog)
/// is not seen.
/// </para>
/// </summary>
internal sealed class ThreadCloseWatcher : IDisposable
{
    private const int WM_CLOSE = 0x0010;

    private readonly Dispatcher _dispatcher;
    private readonly Action _onOutsideClose;
    private bool _attached;
    private volatile int _seen;

    /// <param name="onOutsideClose">Runs on this thread, after the message itself has been dispatched
    /// (so the window it was aimed at has already closed or hidden).</param>
    public ThreadCloseWatcher(Action onOutsideClose)
    {
        _onOutsideClose = onOutsideClose ?? throw new ArgumentNullException(nameof(onOutsideClose));
        _dispatcher = Dispatcher.CurrentDispatcher;
        ComponentDispatcher.ThreadFilterMessage += OnThreadFilterMessage;
        _attached = true;
    }

    /// <summary>
    /// How many posted closes have come off this thread's queue. Set before the message is dispatched,
    /// so code that resumes straight after a <c>ShowDialog()</c> the close ended can already read it.
    /// </summary>
    public int Seen => _seen;

    private void OnThreadFilterMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != WM_CLOSE)
        {
            return;
        }

        _seen++;
        _ = _dispatcher.BeginInvoke(DispatcherPriority.Normal, _onOutsideClose);
    }

    public void Dispose()
    {
        if (!_attached)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            // The event is per thread: unsubscribing from another one would silently remove nothing.
            try { _ = _dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(Dispose)); }
            catch { /* the dispatcher is gone, and so is its thread's subscription */ }
            return;
        }

        _attached = false;
        ComponentDispatcher.ThreadFilterMessage -= OnThreadFilterMessage;
    }
}
