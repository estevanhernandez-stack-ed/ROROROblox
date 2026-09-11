using ROROROblox.App.Localization;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

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
/// <para>
/// WHY IT LIVES IN THE APP (Core string boundary, localization Phase D, 2026-09-07). Every arm is
/// a sentence a viewer reads, and its only caller is the App (the Settings alerts line). It was raw
/// English prose in Core; each arm resolves from resx now (<see cref="Loc"/>), so a live culture
/// toggle re-narrates it. The dynamic "sending to X and Y" arm joins localized channel fragments
/// with a localized connector; a channel's own name (<c>#alerts</c>) and the push provider name are
/// data and pass through verbatim. Product names (Pushover, ntfy, Discord) stay English.
/// </para>
/// </summary>
public static class AlertStatusLine
{
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
    /// mean the arm list existing in two places. NO GLYPH HERE: the triangle belongs to the view,
    /// for the reason <c>MainWindow.xaml</c> already records about the compat banner — presentation
    /// glyphs do not belong in a composer's result.
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

        var routed = config.DestinationsFor(AlertKind.AccountDroppedOut)
            .Concat(config.DestinationsFor(AlertKind.MemoryWarning))
            .Concat(config.DestinationsFor(AlertKind.Recycled))
            .Concat(config.DestinationsFor(AlertKind.UptimeMark))
            .ToArray();

        // MetricBreach is deliberately NOT in that list (2026-09-11). Every kind above is here
        // because the user ticked a checkbox for it, and this sentence reports back what they
        // ticked. MetricBreach now DEFAULTS to Local (see DiscordConfig) precisely because it has
        // no checkbox yet, so counting it would make a fresh install claim alerts are configured
        // when nothing is enabled — and the gate that actually decides whether a breach can fire,
        // MetricAlertsEnabled, lives in settings.json where this composer cannot see it. Add it
        // back in the same commit as the routing control the signed-manifest plan ships.

        if (routed.All(d => d == AlertDestination.None))
        {
            return Info(Loc.Get("AlertStatus_NoAlertsYet"));
        }

        // A dead webhook outranks everything else here. The user believes alerts are configured,
        // the routing dropdown still says "My channel," and nothing is arriving.
        if (mineWebhookRejected && routed.Contains(AlertDestination.Mine))
        {
            return Failure(Loc.Get("AlertStatus_MineWebhookDeleted"));
        }

        if (clanWebhookRejected && routed.Contains(AlertDestination.Clan))
        {
            return Failure(Loc.Get("AlertStatus_ClanWebhookDeleted"));
        }

        if (phoneRejected && routed.Contains(AlertDestination.Phone))
        {
            return Failure(Loc.Get("AlertStatus_PhoneRejected"));
        }

        var needsMine = routed.Contains(AlertDestination.Mine) && string.IsNullOrWhiteSpace(config.MineWebhookUrl);
        var needsClan = routed.Contains(AlertDestination.Clan) && string.IsNullOrWhiteSpace(config.ClanWebhookUrl);

        var needsPhone = routed.Contains(AlertDestination.Phone) && !phoneConfigured;
        if (needsPhone)
        {
            // Same failure class as the missing-webhook arm below: nothing is broken, the user
            // simply believes they finished configuring and did not.
            return Failure(Loc.Get("AlertStatus_PhoneUnfinished"));
        }

        if (needsMine || needsClan)
        {
            // A failure too, and the least obvious of the three: nothing is broken, the user simply
            // believes they finished configuring and did not. The outcome is identical — alerts are
            // not arriving where they think they are.
            return Failure(Loc.Get("AlertStatus_WebhookMissing"));
        }

        if (routed.All(d => d is AlertDestination.None or AlertDestination.Local))
        {
            // NOT a failure: this is exactly what the routing dropdowns say, so it is a report of a
            // choice rather than a report of a problem.
            return Info(Loc.Get("AlertStatus_DesktopOnly"));
        }

        // Name every channel actually in use. With two webhooks configured, "Sending to #alerts"
        // would be a true statement that hides half of where things are going — and the half it
        // would hide is the clan channel, the one with an audience. A channel's own name is data;
        // the fallbacks and the connector are localized.
        var channels = new List<string>();
        if (routed.Contains(AlertDestination.Mine))
        {
            channels.Add(mineChannelName is { Length: > 0 } ? $"#{mineChannelName}" : Loc.Get("AlertStatus_Channel_Mine"));
        }

        if (routed.Contains(AlertDestination.Clan))
        {
            channels.Add(clanChannelName is { Length: > 0 } ? $"#{clanChannelName}" : Loc.Get("AlertStatus_Channel_Clan"));
        }

        if (routed.Contains(AlertDestination.Phone))
        {
            channels.Add(phoneProviderName is { Length: > 0 }
                ? Loc.Format("AlertStatus_Channel_PhoneNamed", phoneProviderName)
                : Loc.Get("AlertStatus_Channel_Phone"));
        }

        return Info(channels.Count == 0
            ? Loc.Get("AlertStatus_SendingToDiscord")
            : Loc.Format("AlertStatus_SendingToChannels", string.Join(Loc.Get("AlertStatus_ListConnector"), channels)));
    }
}
