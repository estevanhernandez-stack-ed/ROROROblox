namespace ROROROblox.App.Updates;

/// <summary>
/// Velopack-backed auto-update probe. Spec §9 Auto-update + §7.1 backoff.
/// Debounced 24h via a timestamp file in LocalAppData. User-decline-able (item 10
/// only checks; download/apply happens via tray "Update Available" menu, item 11).
/// </summary>
public interface IUpdateChecker
{
    Task CheckForUpdatesAsync();
}
