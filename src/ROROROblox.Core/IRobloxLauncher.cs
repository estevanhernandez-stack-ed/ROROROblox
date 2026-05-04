namespace ROROROblox.Core;

/// <summary>
/// Launches Roblox as a specific saved account. Spec §5.6 + §6.2.
/// Internal flow: <see cref="IRobloxApi.GetAuthTicketAsync"/> → build <c>roblox-player:</c> URI →
/// <see cref="IProcessStarter"/>. Caller (MainViewModel, item 9) is responsible for retrieving
/// the cookie via <see cref="IAccountStore"/> beforehand and calling
/// <see cref="IAccountStore.TouchLastLaunchedAsync"/> on a <see cref="LaunchResult.Started"/> result.
/// </summary>
public interface IRobloxLauncher
{
    /// <summary>
    /// Spawn Roblox signed in as the cookie's account. When <paramref name="placeUrl"/> is null,
    /// resolve from <see cref="IAppSettings.GetDefaultPlaceUrlAsync"/> — Roblox's auth handshake
    /// requires a non-empty <c>placelauncherurl</c> (caught at spike-time, see spec §5.6).
    /// </summary>
    Task<LaunchResult> LaunchAsync(string cookie, string? placeUrl = null);
}
