namespace ROROROblox.Core.Discord;

/// <summary>
/// One honest sentence about whether alerts will actually reach the user.
/// <para>
/// The whole point of this feature is a notification arriving on a phone while the user is away
/// from the PC. A desktop toast is the floor — it stops an alert being silently dropped — but
/// someone sitting at the desk can already see that a client died, so "desktop alerts are on"
/// is not the same as "alerts work." This composer refuses to say the reassuring thing when the
/// true thing is that nothing is leaving the machine.
/// </para>
/// <para>
/// Pure, and separated from the Settings window on purpose: which sentence appears in which state
/// is the substance of the feature's honesty, so it belongs in a table of cases a test can pin
/// rather than in a chain of UI branches nobody reads again.
/// </para>
/// </summary>
public static class AlertStatusLine
{
    /// <summary>
    /// <paramref name="mineWebhookRejected"/> / <paramref name="clanWebhookRejected"/> come from
    /// <c>AlertDispatcher</c> — a webhook that returned 404. That state has NO other discovery
    /// path: Discord never tells the user they deleted it, the alerts fall back to desktop, and
    /// everything looks configured. It has to be the loudest thing this line can say.
    /// <para>
    /// <paramref name="mineChannelName"/> is the channel a working webhook reports posting to
    /// (from <c>WebhookProbe</c>), so the user can catch a clan webhook pasted into the personal
    /// slot before the first alert lands in the wrong place.
    /// </para>
    /// </summary>
    public static string Compose(
        DiscordConfig config,
        bool mineWebhookRejected = false,
        bool clanWebhookRejected = false,
        string? mineChannelName = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var routed = new[] { config.DroppedOutDestination, config.MemoryWarningDestination };

        if (routed.All(d => d == AlertDestination.None))
        {
            return "No alerts yet. Pick what you want to hear about above.";
        }

        // A dead webhook outranks everything else here. The user believes alerts are configured,
        // the routing dropdown still says "My channel," and nothing is arriving.
        if (mineWebhookRejected && routed.Contains(AlertDestination.Mine))
        {
            return "Your webhook was deleted, so nothing is reaching your phone. Alerts are falling back to desktop only — make a new webhook and paste it below.";
        }

        if (clanWebhookRejected && routed.Contains(AlertDestination.Clan))
        {
            return "The clan webhook was deleted, so nothing is reaching that channel. Alerts are falling back to desktop only.";
        }

        var needsMine = routed.Contains(AlertDestination.Mine) && string.IsNullOrWhiteSpace(config.MineWebhookUrl);
        var needsClan = routed.Contains(AlertDestination.Clan) && string.IsNullOrWhiteSpace(config.ClanWebhookUrl);

        if (needsMine || needsClan)
        {
            return "You've routed alerts to a Discord channel but haven't pasted a webhook, so they'll only show on this PC — which won't help when you're away from it.";
        }

        if (routed.All(d => d is AlertDestination.None or AlertDestination.Local))
        {
            return "Desktop only. You'll see these at the PC, but nothing will reach your phone.";
        }

        return mineChannelName is { Length: > 0 }
            ? $"Sending to #{mineChannelName}."
            : "Sending to your Discord channel.";
    }
}
