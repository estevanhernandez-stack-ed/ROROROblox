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

    /// <summary>Fan-out sets for the 2026-09-05 kinds. List-only — these kinds postdate the
    /// singular fields, so there is no legacy value to mirror and nothing for an older binary
    /// to misroute (it ignores unknown JSON fields and unknown kinds alike).</summary>
    public IReadOnlyList<AlertDestination> RecycledDestinations { get; init; } = [];

    public IReadOnlyList<AlertDestination> UptimeMarkDestinations { get; init; } = [];

    /// <summary>
    /// Where a <see cref="AlertKind.MetricBreach"/> goes. The one kind that does NOT start empty,
    /// because it is the one kind with no routing checkbox: Settings paints the fan-out sets for
    /// the four older kinds only, so nothing in production ever wrote this list. Empty meant the
    /// host raised a breach, <see cref="AlertRouter"/> resolved it to no destination, and the
    /// dispatcher logged "routed nowhere" — on every install, forever (whole-branch review R-10,
    /// 2026-09-11).
    /// <para>
    /// Defaulting it to <see cref="AlertDestination.Local"/> cannot surprise anyone, because three
    /// gates stand in front of it already: <c>MetricAlertsEnabled</c> is false by default, a rules
    /// file must exist, and a plugin must hold <c>host.metrics.report</c> by the user's own grant.
    /// Local is also where the router falls back when a chosen destination is unconfigured, so this
    /// adds no delivery leg that was not already reachable. Discord and phone routing need the
    /// Settings UI that arrives with the signed-manifest plan.
    /// </para>
    /// The upgrade case is the one worth pinning rather than assuming: every <c>discord.dat</c> on
    /// disk predates this field, and a defaulted property and a deserialized-absent property are
    /// not automatically the same thing. System.Text.Json constructs through the parameterless
    /// constructor and leaves an absent property at its initializer, so the default survives the
    /// round trip — <c>DiscordConfigStoreTests</c> proves it against a real envelope.
    /// </summary>
    public IReadOnlyList<AlertDestination> MetricBreachDestinations { get; init; } = [AlertDestination.Local];

    public IReadOnlyList<Guid> MutedAccountIds { get; init; } = [];

    /// <summary>The effective destination set for a kind — the list when present, else the
    /// migrated singular field. A method, not a property, so the JSON serializer never sees it.</summary>
    public IReadOnlyList<AlertDestination> DestinationsFor(AlertKind kind)
    {
        var (list, single) = kind switch
        {
            AlertKind.AccountDroppedOut => (DroppedOutDestinations, DroppedOutDestination),
            AlertKind.MemoryWarning => (MemoryWarningDestinations, MemoryWarningDestination),
            AlertKind.Recycled => (RecycledDestinations, AlertDestination.None),
            AlertKind.UptimeMark => (UptimeMarkDestinations, AlertDestination.None),
            AlertKind.MetricBreach => (MetricBreachDestinations, AlertDestination.None),
            _ => ((IReadOnlyList<AlertDestination>)[], AlertDestination.None),
        };

        if (list.Count > 0) return list;
        return single == AlertDestination.None ? [] : [single];
    }
}
