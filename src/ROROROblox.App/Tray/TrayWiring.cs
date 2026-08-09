using ROROROblox.Core;

namespace ROROROblox.App.Tray;

/// <summary>
/// Subscribes every tray request event to its handler, and does nothing else.
/// <para>
/// Extracted from App.xaml.cs for F-001. While these eleven lines lived inside a WPF
/// <c>Application</c> they could not be tested at all, so a transposed subscription — Stop all
/// wired to Quit — was a silent behaviour swap that compiled cleanly.
/// </para>
/// </summary>
internal static class TrayWiring
{
    public static void Connect(ITrayService tray, TrayHandlers handlers)
    {
        tray.RequestOpenMainWindow += (_, _) => handlers.OpenMainWindow();
        tray.RequestToggleMutex += (_, _) => handlers.ToggleMutex();
        tray.RequestStopAllInstances += (_, _) => handlers.StopAllInstances();
        tray.RequestQuit += (_, _) => handlers.Quit();
        tray.RequestOpenDiagnostics += (_, _) => handlers.OpenDiagnostics();
        tray.RequestOpenLogs += (_, _) => handlers.OpenLogs();
        tray.RequestOpenPreferences += (_, _) => handlers.OpenPreferences();
        tray.RequestActivateMain += (_, _) => handlers.ActivateMain();
        tray.RequestOpenHistory += (_, _) => handlers.OpenHistory();
        tray.RequestOpenPlugins += (_, _) => handlers.OpenPlugins();
        tray.RequestFocusAccount += (_, id) => handlers.FocusAccount(id);
    }
}
