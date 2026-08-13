namespace ROROROblox.Core;

/// <summary>
/// Per-user app preferences — theme, startup toggles, dismissed warnings and the rest.
/// <para>
/// This summary claimed "First-launch UX prompts to seed; the Preferences dialog allows editing"
/// until v1.21 item 9. Neither had been true for over a cycle: no window reads
/// <see cref="GetDefaultPlaceUrlAsync"/> and none ever wrote it, and per-account favourites
/// (<c>IFavoriteGameStore</c>) replaced the single-URL model entirely. An interface comment
/// describing a dialog that does not exist is worse than no comment, because it is the first thing
/// a reader trusts.
/// </para>
/// </summary>
public interface IAppSettings
{
    /// <summary>
    /// The legacy single-URL default place. <b>Read-only by design — there is deliberately no
    /// setter</b> (F-093, v1.21 item 9).
    /// <para>
    /// It survives so an old <c>settings.json</c> still deserializes and its owner's value is not
    /// silently erased on the next write. Nothing in the app can set it, and nothing in the app
    /// reads it either: <c>MainViewModel</c> launches through
    /// <c>IRobloxLauncher.LaunchAsync(cookie, LaunchTarget, …)</c>, and that path resolves
    /// favourites then falls through to <c>LaunchTarget.Home</c>, ignoring this value on purpose
    /// (<c>RobloxLauncher.cs:353</c>, pinned by
    /// <c>RobloxLauncherTests.LaunchAsync_TypedApi_NoFavoriteDefault_IgnoresLegacySettingsUrl_ResolvesToHome</c>).
    /// The only remaining reader is the legacy string overload of <c>LaunchAsync</c>, which the app
    /// never calls.
    /// </para>
    /// <para>
    /// So deleting the field outright changes nobody's launch target — which is NOT what this
    /// cycle's spec believed when it recommended keeping the read path. That belongs to F-093's
    /// final pass along with the legacy overload it serves, not to a comment fix.
    /// </para>
    /// </summary>
    Task<string?> GetDefaultPlaceUrlAsync();

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
    /// A theme author's answer to "may we raise this theme's interactive-control edge so it meets
    /// the 3:1 contrast floor?", keyed by theme id. <c>null</c> means never asked — the question is
    /// put once per theme and never again. <c>true</c> accepted; <c>false</c> declined, and the
    /// theme then renders exactly as authored.
    /// <para>
    /// Keyed per theme rather than per app because the answer is about somebody's specific work.
    /// Switching to a different custom theme is a different question. Built-in themes are never
    /// asked about (the shortfall is ours to fix), so they never appear here.
    /// </para>
    /// </summary>
    Task<bool?> GetEdgeRemediationAnswerAsync(string themeId);
    Task SetEdgeRemediationAnswerAsync(string themeId, bool accepted);

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
    /// True when the per-row Recycle button should show on every running row instead of only while
    /// the memory warning is latched. Defaults to false (the v1.12 silhouette).
    /// <para>
    /// Recycle was built as the remedy for Roblox's memory leak, and its visibility still says so.
    /// Since v1.14 it also puts an account back in the exact server it was in — a reason to press
    /// it that has nothing to do with RAM. This opt-in makes it reachable then, without changing
    /// what an untouched install looks like.
    /// </para>
    /// </summary>
    Task<bool> GetAlwaysShowRecycleAsync();
    Task SetAlwaysShowRecycleAsync(bool always);

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

    /// <summary>
    /// True when the main window opens in compact mode — the fixed-width, live-rows-only strip the
    /// status bar's Compact button toggles. Defaults to false (expanded).
    /// <para>
    /// Sticky because the Welcome tour pitches compact as a second-monitor workflow, and a
    /// second-monitor layout that resets on every launch is not a workflow. The toggle is on the
    /// main window, not in Preferences: this is a per-session view state the user flips constantly,
    /// and its home is where it is used.
    /// </para>
    /// </summary>
    Task<bool> GetCompactModeAsync();
    Task SetCompactModeAsync(bool compact);
}
