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
    /// <summary>
    /// <paramref name="cadence"/> is the quiet period per kind, and it arrives as an argument
    /// because it is a user setting as of v1.33 — <c>AlertCadenceMinutes</c> and
    /// <c>AlertCadenceOverridesJson</c> on <c>IAppSettings</c>. It was a <c>static readonly
    /// TimeSpan Cooldown</c> here from v1.0 to v1.32, which is why
    /// <see cref="AlertCadence.DefaultQuietPeriod"/> is still five minutes: an upgrade must change
    /// nobody's pace. <c>null</c> means <see cref="AlertCadence.Default"/>, which is what every
    /// caller that does not care about cadence passes.
    /// <para>
    /// <paramref name="lastSentPerAccount"/> is keyed by <see cref="AlertCooldownKey"/>: (account,
    /// KIND), not by account alone, and for a metric breach (account, metric id).
    /// </para>
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
        bool phoneConfigured = false,
        AlertCadence? cadence = null)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(lastSentPerAccount);

        var muted = config.MutedAccountIds.ToHashSet();
        var pace = cadence ?? AlertCadence.Default;

        return pending
            .Where(t => !muted.Contains(t.AccountId))
            // ONCE PER EVENT, BEFORE THE FAN-OUT. This clause runs per trigger and the GroupBy
            // below it is what multiplies a kind out across its destinations, so a kind going to
            // Discord AND the phone AND the desktop consults one cooldown slot once. Moving this
            // check inside the SelectMany would read the same and mean something else — three
            // destinations would each ask, and a refactor that did it would be green everywhere
            // except the test that counts the consults (AlertCadenceTests).
            .Where(t => MaySpeak(t, lastSentPerAccount, nowUtc, pace.For(t.Kind)))
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

    /// <summary>
    /// Whether this trigger's slot has gone quiet long enough to speak again.
    /// <para>
    /// The comparison is <c>&gt;=</c>, not <c>&gt;</c>, and that is a fix rather than a taste
    /// (v1.33). Measured 2026-10-04: a crossing at 23:33:03 produced nothing and the next at
    /// 23:34:03 produced an alert, because the first landed exactly five minutes after the previous
    /// send — to the second — and <c>&gt;</c> dropped it. Whatever samples the condition does so on
    /// a fixed tick, so "exactly the cadence" is not a rare coincidence here; it is the common case
    /// for anything the 30-second routine tick drives. A cadence of N must mean the repeat at N
    /// speaks, or the interval a user picks is quietly N plus one tick.
    /// </para>
    /// <para>
    /// A quiet period of zero is "every time" — the codebase's existing idiom for a deliberate off
    /// (<c>MemoryCapMb</c>), and it short-circuits rather than relying on <c>now - last &gt;= 0</c>,
    /// which would also have to trust the clock not to run backwards.
    /// </para>
    /// </summary>
    private static bool MaySpeak(
        AlertTrigger trigger,
        IReadOnlyDictionary<AlertCooldownKey, DateTimeOffset> lastSentPerAccount,
        DateTimeOffset nowUtc,
        TimeSpan quietPeriod)
    {
        if (quietPeriod <= TimeSpan.Zero) return true;
        return !lastSentPerAccount.TryGetValue(AlertCooldownKey.For(trigger), out var last)
            || nowUtc - last >= quietPeriod;
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
