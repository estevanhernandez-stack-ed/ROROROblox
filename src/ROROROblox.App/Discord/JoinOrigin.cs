namespace ROROROblox.App.Discord;

/// <summary>
/// Where an inbound Discord join request arrived from. Drives whether
/// <see cref="ROROROblox.App.ViewModels.MainViewModel.HandleDiscordJoinAsync"/> confirms before
/// launching — see that method's remarks for the full reasoning.
/// <para>
/// This is a distinction of ORIGIN, not of destination risk. A <see cref="DiscordClient"/> join can
/// only fire after the user turned Join on and a friend received a secret RoRoRo deliberately
/// published into that user's own presence — it is trusted by construction. A
/// <see cref="UriHandler"/> join arrives via the <c>roblox-rororo:</c> OS protocol handler, which
/// any local process, <c>.url</c> file, or browser navigation can trigger; nothing in the URI proves
/// Discord — or anyone in particular — sent it.
/// </para>
/// </summary>
internal enum JoinOrigin
{
    /// <summary>Discord's in-client Join button, via <see cref="DiscordPresenceService.JoinRequested"/>.</summary>
    DiscordClient,

    /// <summary>
    /// The <c>roblox-rororo:</c> OS protocol handler — cold start or relayed from a second
    /// instance, via <c>App.JoinRequested</c>.
    /// </summary>
    UriHandler,
}
