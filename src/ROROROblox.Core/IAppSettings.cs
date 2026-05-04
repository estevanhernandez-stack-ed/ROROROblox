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
}
