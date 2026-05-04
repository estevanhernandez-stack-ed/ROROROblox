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
    /// </summary>
    Task<LaunchResult> LaunchAsync(string cookie, LaunchTarget target);

    /// <summary>
    /// Legacy string-based overload. Pasted URLs run through <see cref="LaunchTarget.FromUrl"/>
    /// — a private-server share URL becomes a <see cref="LaunchTarget.PrivateServer"/> automatically.
    /// Null/empty placeUrl falls back to <see cref="LaunchTarget.DefaultGame"/>.
    /// </summary>
    Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null);
}
