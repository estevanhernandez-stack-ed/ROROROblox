using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core;

/// <summary>
/// One-shot save helper for the Join-by-link paste flow. Routes a resolved
/// <see cref="LaunchTarget"/> to the right store's <c>AddAsync</c> when the user opted in
/// via the dialog's "Save to my library" checkbox. Lives in Core (not the App ViewModel)
/// so the dispatch logic is unit-testable without WPF. v1.3.x save-pasted-links cycle.
/// </summary>
/// <remarks>
/// Save failures NEVER bubble — the launch is the user's primary intent; save is the
/// optional sweetener. All exceptions caught and logged at warning level. Spec §7.
/// </remarks>
public static class JoinByLinkSave
{
    public static async Task ApplyAsync(
        IRobloxApi api,
        IFavoriteGameStore favoriteGames,
        IPrivateServerStore privateServers,
        LaunchTarget target,
        bool saveToLibrary,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(favoriteGames);
        ArgumentNullException.ThrowIfNull(privateServers);
        ArgumentNullException.ThrowIfNull(target);

        if (!saveToLibrary) return;

        var log = logger ?? NullLogger.Instance;

        try
        {
            switch (target)
            {
                case LaunchTarget.Place place:
                {
                    var meta = await api.GetGameMetadataByPlaceIdAsync(place.PlaceId).ConfigureAwait(false);
                    if (meta is null)
                    {
                        log.LogWarning("Skipping save for pasted Place {PlaceId}: metadata returned null.", place.PlaceId);
                        return;
                    }
                    await favoriteGames.AddAsync(meta.PlaceId, meta.UniverseId, meta.Name, meta.IconUrl).ConfigureAwait(false);
                    break;
                }

                case LaunchTarget.PrivateServer ps:
                {
                    var meta = await api.GetGameMetadataByPlaceIdAsync(ps.PlaceId).ConfigureAwait(false);
                    if (meta is null)
                    {
                        log.LogWarning("Skipping save for pasted PrivateServer at place {PlaceId}: metadata returned null.", ps.PlaceId);
                        return;
                    }
                    // Mirrors SquadLaunchWindow.xaml.cs:294 — Name defaults to PlaceName; user
                    // can rename later via the existing context menu (cycle #2).
                    await privateServers.AddAsync(ps.PlaceId, ps.Code, ps.Kind, meta.Name, meta.Name, meta.IconUrl).ConfigureAwait(false);
                    break;
                }

                // DefaultGame and FollowFriend are not save-eligible from this surface.
                default:
                    return;
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to save pasted target {Target}; launching anyway.", target);
        }
    }
}
