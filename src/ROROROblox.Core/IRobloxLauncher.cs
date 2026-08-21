namespace ROROROblox.Core;

/// <summary>
/// Launches Roblox as a specific saved account. Spec §5.6 + §6.2.
/// Internal flow: <see cref="IRobloxApi.GetAuthTicketAsync"/> -> build <c>roblox-player:</c> URI ->
/// <see cref="IProcessStarter"/>. Caller (MainViewModel, item 9) is responsible for retrieving
/// the cookie via <see cref="IAccountStore"/> beforehand and calling
/// <see cref="IAccountStore.TouchLastLaunchedAsync"/> on a <see cref="LaunchResult.Started"/> result.
/// </summary>
public interface IRobloxLauncher
{
    /// <summary>
    /// Launch with a typed target. <see cref="LaunchTarget.DefaultGame"/> resolves through the
    /// favorites store + app settings; <see cref="LaunchTarget.Place"/> targets a specific public
    /// place; <see cref="LaunchTarget.PrivateServer"/> targets a VIP server with placeId +
    /// accessCode; <see cref="LaunchTarget.FollowFriend"/> follows a friend's userId.
    /// <paramref name="browserTrackerId"/> is the account's stable persisted tracker id
    /// (v1.8.1 trust hygiene); null falls back to a random one-shot value.
    /// </summary>
    Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target, int? fpsCap = null, long? browserTrackerId = null);

    // The legacy string overload was DELETED by F-093. It existed to resolve a place URL through
    // three tiers, the last of which was AppSettings.DefaultPlaceUrl — a setting the app referenced
    // zero times and had no UI to write. Nothing called this overload either: MainViewModel launches
    // through the LaunchTarget one above and always has. Deleting the setting without the overload
    // it served would have left a tier resolving against a method that no longer exists, so they
    // went together, which is what F-093's row asked for.
}
