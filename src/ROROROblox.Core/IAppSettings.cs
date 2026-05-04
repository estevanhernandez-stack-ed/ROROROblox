namespace ROROROblox.Core;

/// <summary>
/// Per-user app preferences. Currently just the default Roblox place URL — the
/// <c>placelauncherurl</c> the launcher uses when the caller doesn't supply one.
/// First-launch UX (item 9) prompts the user to seed this; main-window settings (item 9)
/// allows editing.
/// </summary>
public interface IAppSettings
{
    Task<string?> GetDefaultPlaceUrlAsync();
    Task SetDefaultPlaceUrlAsync(string url);
}
