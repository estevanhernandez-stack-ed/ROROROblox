namespace ROROROblox.App.Tray;

/// <summary>
/// Remembers the tray service the container built, so <c>App.OnExit</c> can dispose it on the UI
/// thread before the container is disposed off it.
/// <para>
/// WHY THIS EXISTS. <c>App.OnExit</c> disposes the container on a pool thread and blocks the UI thread
/// on a bounded wait. Hardcodet 2.0.1's <c>TaskbarIcon.Dispose</c> marshals to the dispatcher, so when
/// the container reached the tray the two threads waited on each other until the two-second timeout:
/// every exit lost two seconds, the icon was never removed, and nothing created before the tray was
/// ever disposed. Measured with two stack captures on a real exit, 2026-11-16.
/// </para>
/// <para>
/// WHY NOT ASK THE CONTAINER. <c>GetService&lt;ITrayService&gt;()</c> on an exit that happens before
/// anything resolved the tray would construct one, icon and all, only to dispose it. This holds the
/// tray only if one was actually built.
/// </para>
/// <para>
/// Deliberately not <see cref="IDisposable"/>: the container would then dispose it too, on the pool
/// thread, which is the call this class exists to get ahead of.
/// </para>
/// </summary>
internal sealed class BuiltTray
{
    private IDisposable? _tray;

    /// <summary>Called by the tray's registration when it constructs the tray.</summary>
    public void Set(IDisposable tray)
    {
        ArgumentNullException.ThrowIfNull(tray);
        Interlocked.Exchange(ref _tray, tray);
    }

    /// <summary>
    /// Disposes the tray if one was built and has not been handed over already. Call on the UI
    /// thread. Returns whether it disposed anything.
    /// </summary>
    public bool DisposeIfBuilt()
    {
        var tray = Interlocked.Exchange(ref _tray, null);
        if (tray is null)
        {
            return false;
        }

        tray.Dispose();
        return true;
    }
}
