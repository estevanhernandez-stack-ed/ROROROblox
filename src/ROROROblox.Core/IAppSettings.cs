namespace ROROROblox.Core;

/// <summary>
/// Per-user app preferences. Currently the default Roblox place URL — the
/// <c>placelauncherurl</c> the launcher uses when the caller doesn't supply one — and
/// startup-related toggles (auto-launch the main account when the app starts).
/// First-launch UX prompts to seed; the Preferences dialog allows editing.
/// </summary>
public interface IAppSettings
{
    Task<string?> GetDefaultPlaceUrlAsync();
    Task SetDefaultPlaceUrlAsync(string url);

    /// <summary>
    /// True when ROROROblox should launch the user's main account into its current per-row
    /// game pick a moment after the app finishes starting. Defaults to false; the user opts in
    /// via the Preferences dialog. Pairs with run-on-login for hands-free login → playing flow.
    /// </summary>
    Task<bool> GetLaunchMainOnStartupAsync();
    Task SetLaunchMainOnStartupAsync(bool enabled);

    /// <summary>
    /// Active theme id from <c>%LOCALAPPDATA%\ROROROblox\themes\</c> (or a built-in id like
    /// "brand"). Empty / unknown id falls back to the "brand" built-in at startup.
    /// </summary>
    Task<string?> GetActiveThemeIdAsync();
    Task SetActiveThemeIdAsync(string themeId);

    /// <summary>
    /// True after the user has dismissed the "Bloxstrap will override per-account FPS"
    /// banner. Persisted so the banner does not re-render on every launch.
    /// </summary>
    Task<bool> GetBloxstrapWarningDismissedAsync();
    Task SetBloxstrapWarningDismissedAsync(bool value);

    /// <summary>
    /// The canonical signature (see <c>MainViewModel.ComputeFpsCapSignature</c>) of the distinct
    /// FPS-cap set that was in effect the last time the user dismissed the FPS-cap mismatch
    /// banner. <c>null</c> means nothing has ever been dismissed. Persisted so the banner does
    /// not re-render on every launch for a mismatch the user already acknowledged, but DOES
    /// re-render if the set of distinct caps later changes to something not covered by this
    /// signature — dismissal is scoped to the configuration, not "forever."
    /// </summary>
    Task<string?> GetDismissedFpsCapWarningSignatureAsync();
    Task SetDismissedFpsCapWarningSignatureAsync(string? signature);

    /// <summary>
    /// True when the idle-alert toast should stay silent. Defaults to false (alerts on).
    /// The user opts out via the Preferences dialog.
    /// </summary>
    Task<bool> GetMuteIdleAlertsAsync();
    Task SetMuteIdleAlertsAsync(bool muted);

    /// <summary>
    /// Minutes of inactivity before the idle-warn line fires. Defaults to 15. A non-positive
    /// stored or requested value is guarded back to 15 rather than treated as "disabled."
    /// </summary>
    Task<int> GetIdleWarnThresholdMinutesAsync();
    Task SetIdleWarnThresholdMinutesAsync(int minutes);

    /// <summary>
    /// True when Squad Launch should wait for each account to fully land in the game before
    /// dispatching the next. Defaults to false (fire all at once). The user opts in via the
    /// Squad Launch checkbox.
    /// </summary>
    Task<bool> GetCarefulSquadLaunchAsync();
    Task SetCarefulSquadLaunchAsync(bool careful);

    /// <summary>
    /// True when streamer mode is on — the account manager shows fake identities instead of real
    /// names/avatars. Sticky across launches (a streamer wants it reliably on). Defaults to false.
    /// </summary>
    Task<bool> GetStreamerModeAsync();
    Task SetStreamerModeAsync(bool enabled);

    /// <summary>
    /// True when the memory watchdog should sample and react to system/process memory pressure.
    /// Defaults to true. File-only today — there is no Preferences dialog wiring for this or the
    /// three memory settings below it (corrected 2026-08-01, final-branch review finding 6; a
    /// prior version of this comment claimed a Preferences-dialog opt-out that does not exist).
    /// Editable only by hand-editing <c>settings.json</c> until that UI ships.
    /// </summary>
    Task<bool> GetMemoryWatchdogEnabledAsync();
    Task SetMemoryWatchdogEnabledAsync(bool enabled);

    /// <summary>
    /// MB of physical memory the watchdog reserves for the system before triggering. <c>null</c>
    /// means the user has never set this — the composition root derives it from installed RAM via
    /// <see cref="Diagnostics.MemoryDefaults.ReserveMb"/>. A non-null value is a deliberate user
    /// override and must never be silently re-derived over. File-only today — no Preferences
    /// dialog wiring exists yet (see <see cref="GetMemoryWatchdogEnabledAsync"/>).
    /// </summary>
    Task<int?> GetMemoryReserveMbAsync();
    Task SetMemoryReserveMbAsync(int? reserveMb);

    /// <summary>
    /// Per-client MB cap the watchdog enforces. <c>null</c> means the user has never set this —
    /// derived from installed RAM via <see cref="Diagnostics.MemoryDefaults.CapMb"/>. <c>0</c> is a
    /// distinct, meaningful user choice: it disables the cap trigger entirely, which is why this is
    /// nullable rather than sentinel-zero — zero and unset must stay distinguishable. File-only
    /// today — no Preferences dialog wiring exists yet (see <see cref="GetMemoryWatchdogEnabledAsync"/>).
    /// </summary>
    Task<int?> GetMemoryCapMbAsync();
    Task SetMemoryCapMbAsync(int? capMb);

    /// <summary>
    /// Minutes of projected time-to-cap below which the watchdog surfaces a warning. Defaults to
    /// 120. Not hardware-derived — this is a UX pacing knob, not a hardware constraint. File-only
    /// today — no Preferences dialog wiring exists yet (see <see cref="GetMemoryWatchdogEnabledAsync"/>).
    /// </summary>
    Task<int> GetProjectionWarnMinutesAsync();
    Task SetProjectionWarnMinutesAsync(int minutes);
}
