namespace ROROROblox.App.AppLifecycle;

/// <summary>What the main window does with one close request.</summary>
internal enum CloseDisposition
{
    /// <summary>Let the close go through: the app is already exiting.</summary>
    Proceed,

    /// <summary>The request came from outside the process: end RoRoRo through the tray's Quit path.</summary>
    QuitApp,

    /// <summary>Everything else, including every close made inside the app: cancel it and hide to the tray.</summary>
    HideToTray,
}

/// <summary>
/// The decision table for <c>MainWindow.OnClosing</c>, kept pure so the three outcomes can be pinned
/// without a window. The asymmetry is deliberate: hiding is the safe default, and only a close that
/// <see cref="ExternalCloseWatcher"/> positively identified as arriving from outside the process
/// quits. A mistake in the other direction would make the X button quit RoRoRo.
/// </summary>
internal static class CloseRequestPolicy
{
    public static CloseDisposition Decide(bool isShuttingDown, bool isExternalClose)
    {
        if (isShuttingDown)
        {
            return CloseDisposition.Proceed;
        }

        return isExternalClose ? CloseDisposition.QuitApp : CloseDisposition.HideToTray;
    }
}
