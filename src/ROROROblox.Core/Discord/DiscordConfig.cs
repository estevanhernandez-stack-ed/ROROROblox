namespace ROROROblox.Core.Discord;

/// <summary>Where an alert goes. <see cref="None"/> means the trigger is off entirely.</summary>
public enum AlertDestination
{
    None,
    Local,
    Mine,
    Clan,
}

/// <summary>
/// Discord integration settings. Everything defaults off — nothing leaves the machine until the
/// user turns it on. Webhook URLs are bearer credentials; the store encrypts this whole record
/// with DPAPI (see <see cref="DiscordConfigStore"/>).
/// </summary>
public sealed record DiscordConfig
{
    public bool PresenceEnabled { get; init; }
    public bool JoinEnabled { get; init; }
    public string? MineWebhookUrl { get; init; }
    public string? ClanWebhookUrl { get; init; }
    public AlertDestination DroppedOutDestination { get; init; } = AlertDestination.None;
    public AlertDestination MemoryWarningDestination { get; init; } = AlertDestination.None;
    public IReadOnlyList<Guid> MutedAccountIds { get; init; } = [];
}
