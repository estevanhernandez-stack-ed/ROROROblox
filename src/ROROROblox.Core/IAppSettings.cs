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
}
