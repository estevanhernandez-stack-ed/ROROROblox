namespace ROROROblox.Core.Discord;

/// <summary>One alert ready to send: where, what kind, and which accounts it covers.</summary>
public sealed record RoutedAlert(
    AlertDestination Destination,
    AlertKind Kind,
    IReadOnlyList<AlertTrigger> Triggers);

/// <summary>
/// The cooldown slot a trigger occupies: (account, kind) for every kind, plus the metric id for
/// <see cref="AlertKind.MetricBreach"/>. Built ONLY by <see cref="For"/>, which both
/// <see cref="AlertRouter.Route"/> (the check) and <c>AlertDispatcher</c> (the stamp) call, so the
/// check and the stamp cannot disagree about which slot an alert used — and a test cannot seed a
/// slot the router would never look up.
/// </summary>
public readonly record struct AlertCooldownKey
{
    private AlertCooldownKey(Guid accountId, AlertKind kind, string? metricId)
    {
        AccountId = accountId;
        Kind = kind;
        MetricId = metricId;
    }

    public Guid AccountId { get; }

    public AlertKind Kind { get; }

    /// <summary>The metric id for a metric breach; null for every other kind.</summary>
    public string? MetricId { get; }

    public static AlertCooldownKey For(AlertTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        // GameName is where a metric breach carries its metric id (see AlertTrigger). Every other
        // kind carries a real game name there, which must NOT split the slot: a client flapping
        // between two games is still one flapping client.
        return new AlertCooldownKey(
            trigger.AccountId,
            trigger.Kind,
            trigger.Kind == AlertKind.MetricBreach ? trigger.GameName : null);
    }
}

/// <summary>
/// Decides what actually gets sent. Pure — the caller supplies "now" and the per-account
/// last-sent map, so cooldown behavior is a table of cases rather than a test that sleeps.
/// <para>
/// Routing is per-trigger and muting is per-account, which keeps the configuration surface at two
/// controls. The full matrix (8 accounts x 2 triggers x 3 destinations) is 48 switches nobody
/// finishes setting up.
/// </para>
/// </summary>
public static class AlertRouter
{
    /// <summary>Per-account quiet period. A client that flaps must not page someone repeatedly.</summary>
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    /// <summary>
    /// <paramref name="lastSentPerAccount"/> is keyed by <see cref="AlertCooldownKey"/>: (account,
    /// KIND), not by account alone, and for a metric breach (account, metric id).
    /// <para>
    /// Measured live on 2026-08-04: a memory warning at 00:13:55 stamped the cooldown for two
    /// accounts, and a genuine client close at 00:14:21 was swallowed because it fell inside that
    /// window. The cooldown exists to stop ONE flapping condition paging someone repeatedly — it
    /// was never meant to let a memory warning silence a crash. Different kinds are different
    /// news, and the drop is the more urgent of the two.
    /// </para>
    /// <para>
    /// The same argument, one level down (2026-09-15): a Points stall and a Diamonds alert for one
    /// account in the same plugin read are two different things to know, and keyed per (account,
    /// kind) whichever sent first silenced the other for five minutes. So a metric breach's slot
    /// adds its metric id. Two rules on ONE metric still share a slot, because the key is the metric,
    /// not the rule.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RoutedAlert> Route(
        IReadOnlyList<AlertTrigger> pending,
        DiscordConfig config,
        IReadOnlyDictionary<AlertCooldownKey, DateTimeOffset> lastSentPerAccount,
        DateTimeOffset nowUtc,
        bool phoneConfigured = false)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(lastSentPerAccount);

        var muted = config.MutedAccountIds.ToHashSet();

        return pending
            .Where(t => !muted.Contains(t.AccountId))
            .Where(t => !lastSentPerAccount.TryGetValue(AlertCooldownKey.For(t), out var last) || nowUtc - last > Cooldown)
            .GroupBy(t => t.Kind)
            .SelectMany(group =>
            {
                // One RoutedAlert per destination (fan-out, 2026-09-05): the dispatcher's switch
                // and its cooldown stamping are unchanged — stamping the same cooldown key at the
                // same instant once per destination is idempotent.
                var triggers = group.ToList();
                return Resolve(group.Key, config, phoneConfigured)
                    .Select(destination => new RoutedAlert(destination, group.Key, triggers));
            })
            .ToList();
    }

    private static IReadOnlyList<AlertDestination> Resolve(AlertKind kind, DiscordConfig config, bool phoneConfigured)
    {
        var resolved = new List<AlertDestination>();
        foreach (var wanted in config.DestinationsFor(kind))
        {
            // Routed somewhere that isn't configured yet -> fall back to the desktop
            // notification rather than dropping it. A silently vanishing alert is the worst
            // outcome here. ("Configured" for phone is the caller's composite: provider
            // credentials present AND the endpoint not rejected this session — the phone config
            // lives in its own record, notify.dat, so it arrives as a bool.)
            var effective = wanted switch
            {
                AlertDestination.Mine when string.IsNullOrWhiteSpace(config.MineWebhookUrl) => AlertDestination.Local,
                AlertDestination.Clan when string.IsNullOrWhiteSpace(config.ClanWebhookUrl) => AlertDestination.Local,
                AlertDestination.Phone when !phoneConfigured => AlertDestination.Local,
                _ => wanted,
            };

            // Dedupe, order-preserving: two unconfigured destinations both falling back must
            // page the desktop once, not twice.
            if (effective != AlertDestination.None && !resolved.Contains(effective))
            {
                resolved.Add(effective);
            }
        }

        return resolved;
    }
}
