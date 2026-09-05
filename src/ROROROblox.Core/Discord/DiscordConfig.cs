namespace ROROROblox.Core.Discord;

/// <summary>Where an alert goes. <see cref="None"/> means the trigger is off entirely.</summary>
public enum AlertDestination
{
    None,
    Local,
    Mine,
    Clan,

    /// <summary>The user's phone via a push provider (Pushover/ntfy — see
    /// <c>ROROROblox.Core.Notify.PhoneNotifyConfig</c>). Appended after Clan: this enum is
    /// serialized numerically inside discord.dat, so member order is a wire format.</summary>
    Phone,
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

    /// <summary>
    /// Multi-destination routing (Este's smoke feedback, 2026-09-05): each alert kind fans out
    /// to a SET of destinations — desktop AND phone AND a channel is a legitimate answer. The
    /// singular fields above stay as the rollback mirror: Settings writes them as the set's
    /// first entry, so an older binary reading only the singular field still routes SOMEWHERE
    /// instead of silently dropping (the destination-4 hazard the phone spec records).
    /// Empty list + singular set = a pre-fanout blob; <see cref="DestinationsFor"/> migrates on
    /// read, so no store rewrite is needed.
    /// </summary>
    public IReadOnlyList<AlertDestination> DroppedOutDestinations { get; init; } = [];

    public IReadOnlyList<AlertDestination> MemoryWarningDestinations { get; init; } = [];

    public IReadOnlyList<Guid> MutedAccountIds { get; init; } = [];

    /// <summary>The effective destination set for a kind — the list when present, else the
    /// migrated singular field. A method, not a property, so the JSON serializer never sees it.</summary>
    public IReadOnlyList<AlertDestination> DestinationsFor(AlertKind kind)
    {
        var (list, single) = kind switch
        {
            AlertKind.AccountDroppedOut => (DroppedOutDestinations, DroppedOutDestination),
            AlertKind.MemoryWarning => (MemoryWarningDestinations, MemoryWarningDestination),
            _ => ((IReadOnlyList<AlertDestination>)[], AlertDestination.None),
        };

        if (list.Count > 0) return list;
        return single == AlertDestination.None ? [] : [single];
    }
}
