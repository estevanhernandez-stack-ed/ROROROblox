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
    /// <summary>
    /// A composed status sentence and whether it reports a FAILURE (F-094).
    /// <para>
    /// <c>Compose</c> returned a bare string and the view painted every arm in <c>CyanBrush</c> —
    /// the accent, which is the treatment a success gets. So "your webhook was deleted, so nothing
    /// is reaching your phone" was rendered in exactly the same colour as "sending to #alerts". The
    /// sentence said one thing and the styling said another, and styling is what a user reads first.
    /// </para>
    /// <para>
    /// Severity travels with the sentence rather than being re-derived by the caller, which would
    /// mean the arm list existing in two places. The shape is copied from
    /// <c>ThemeStatusSummary.Line</c> rather than invented. NO GLYPH HERE: the triangle belongs to
    /// the view, for the reason <c>MainWindow.xaml</c> already records about the compat banner —
    /// presentation glyphs do not belong in a composer's result.
    /// </para>
    /// </summary>
    public readonly record struct Line(bool IsFailure, string Text);

    /// <summary>Reports a state the user believes is working and is not.</summary>
    private static Line Failure(string text) => new(true, text);

    /// <summary>Reports a state that is what the user asked for, including "nothing configured".</summary>
    private static Line Info(string text) => new(false, text);

    public static Line Compose(
        DiscordConfig config,
        bool mineWebhookRejected = false,
        bool clanWebhookRejected = false,
        string? mineChannelName = null,
        string? clanChannelName = null,
        bool phoneRejected = false,
        bool phoneConfigured = false,
        string? phoneProviderName = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var routed = new[] { config.DroppedOutDestination, config.MemoryWarningDestination };

        if (routed.All(d => d == AlertDestination.None))
        {
            return Info("No alerts yet. Pick what you want to hear about above.");
        }

        // A dead webhook outranks everything else here. The user believes alerts are configured,
        // the routing dropdown still says "My channel," and nothing is arriving.
        if (mineWebhookRejected && routed.Contains(AlertDestination.Mine))
        {
            return Failure("Your webhook was deleted, so nothing is reaching your phone. Alerts are falling back to desktop only — make a new webhook and paste it below.");
        }

        if (clanWebhookRejected && routed.Contains(AlertDestination.Clan))
        {
            return Failure("The clan webhook was deleted, so nothing is reaching that channel. Alerts are falling back to desktop only.");
        }

        if (phoneRejected && routed.Contains(AlertDestination.Phone))
        {
            return Failure("Your push service rejected the saved key, so nothing is reaching your phone. Alerts are falling back to desktop only — check the key below and save it again.");
        }

        var needsMine = routed.Contains(AlertDestination.Mine) && string.IsNullOrWhiteSpace(config.MineWebhookUrl);
        var needsClan = routed.Contains(AlertDestination.Clan) && string.IsNullOrWhiteSpace(config.ClanWebhookUrl);

        var needsPhone = routed.Contains(AlertDestination.Phone) && !phoneConfigured;
        if (needsPhone)
        {
            // Same failure class as the missing-webhook arm below: nothing is broken, the user
            // simply believes they finished configuring and did not.
            return Failure("You've routed alerts to your phone but haven't finished setting up the push service below, so they'll only show on this PC.");
        }

        if (needsMine || needsClan)
        {
            // A failure too, and the least obvious of the three: nothing is broken, the user simply
            // believes they finished configuring and did not. The outcome is identical — alerts are
            // not arriving where they think they are.
            return Failure("You've routed alerts to a Discord channel but haven't pasted a webhook, so they'll only show on this PC — which won't help when you're away from it.");
        }

        if (routed.All(d => d is AlertDestination.None or AlertDestination.Local))
        {
            // NOT a failure: this is exactly what the routing dropdowns say, so it is a report of a
            // choice rather than a report of a problem.
            return Info("Desktop only. You'll see these at the PC, but nothing will reach your phone.");
        }

        // Name every channel actually in use. With two webhooks configured, "Sending to #alerts"
        // would be a true statement that hides half of where things are going — and the half it
        // would hide is the clan channel, the one with an audience.
        var channels = new List<string>();
        if (routed.Contains(AlertDestination.Mine))
        {
            channels.Add(mineChannelName is { Length: > 0 } ? $"#{mineChannelName}" : "your channel");
        }

        if (routed.Contains(AlertDestination.Clan))
        {
            channels.Add(clanChannelName is { Length: > 0 } ? $"#{clanChannelName}" : "the clan channel");
        }

        if (routed.Contains(AlertDestination.Phone))
        {
            channels.Add(phoneProviderName is { Length: > 0 } ? $"your phone ({phoneProviderName})" : "your phone");
        }

        return Info(channels.Count == 0
            ? "Sending to your Discord channel."
            : $"Sending to {string.Join(" and ", channels)}.");
    }
}
