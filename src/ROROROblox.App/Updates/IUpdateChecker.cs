namespace ROROROblox.App.Updates;

/// <summary>
/// Velopack-backed auto-update. Spec §9 Auto-update + §7.1 backoff.
/// Debounced 24h via a timestamp file in LocalAppData. Checks, downloads, and stages in
/// one pass; the staged package applies when the app exits (item 11, wired 2026-09-05 —
/// this comment said "tray menu, item 11" for the sixteen months it was check-only).
/// </summary>
public interface IUpdateChecker
{
    Task CheckForUpdatesAsync();
}
