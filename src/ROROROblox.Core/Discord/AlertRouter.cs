namespace ROROROblox.Core.Discord;

/// <summary>One alert ready to send: where, what kind, and which accounts it covers.</summary>
public sealed record RoutedAlert(
    AlertDestination Destination,
    AlertKind Kind,
    IReadOnlyList<AlertTrigger> Triggers);

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
    /// <paramref name="lastSentPerAccount"/> is keyed by (account, KIND), not by account alone.
    /// <para>
    /// Measured live on 2026-08-04: a memory warning at 00:13:55 stamped the cooldown for two
    /// accounts, and a genuine client close at 00:14:21 was swallowed because it fell inside that
    /// window. The cooldown exists to stop ONE flapping condition paging someone repeatedly — it
    /// was never meant to let a memory warning silence a crash. Different kinds are different
    /// news, and the drop is the more urgent of the two.
    /// </para>
    /// </summary>
    public static IReadOnlyList<RoutedAlert> Route(
        IReadOnlyList<AlertTrigger> pending,
        DiscordConfig config,
        IReadOnlyDictionary<(Guid AccountId, AlertKind Kind), DateTimeOffset> lastSentPerAccount,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(lastSentPerAccount);

        var muted = config.MutedAccountIds.ToHashSet();

        return pending
            .Where(t => !muted.Contains(t.AccountId))
            .Where(t => !lastSentPerAccount.TryGetValue((t.AccountId, t.Kind), out var last) || nowUtc - last > Cooldown)
            .GroupBy(t => t.Kind)
            .Select(group => new { group.Key, Triggers = group.ToList(), Destination = Resolve(group.Key, config) })
            .Where(x => x.Destination != AlertDestination.None)
            .Select(x => new RoutedAlert(x.Destination, x.Key, x.Triggers))
            .ToList();
    }

    private static AlertDestination Resolve(AlertKind kind, DiscordConfig config)
    {
        var wanted = kind switch
        {
            AlertKind.AccountDroppedOut => config.DroppedOutDestination,
            AlertKind.MemoryWarning => config.MemoryWarningDestination,
            _ => AlertDestination.None,
        };

        // Routed somewhere that isn't configured yet -> fall back to the desktop notification
        // rather than dropping it. A silently vanishing alert is the worst outcome here.
        return wanted switch
        {
            AlertDestination.Mine when string.IsNullOrWhiteSpace(config.MineWebhookUrl) => AlertDestination.Local,
            AlertDestination.Clan when string.IsNullOrWhiteSpace(config.ClanWebhookUrl) => AlertDestination.Local,
            _ => wanted,
        };
    }
}
