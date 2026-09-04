using Microsoft.Extensions.Logging;
using ROROROblox.Core;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// Turns triggers into delivered alerts: route (mute, coalesce, cooldown), then send.
/// <para>
/// Holds the per-account last-sent map that <see cref="AlertRouter"/> reads, so the router itself
/// stays pure. A destination whose webhook 404s is marked rejected and surfaced in Settings rather
/// than retried — a deleted webhook does not come back.
/// </para>
/// <para>
/// Degrade-safe like every other Discord surface: an alert is a passenger. Nothing here may throw
/// into a caller, because the callers are the memory watchdog and the presence path, and neither
/// of those may be taken down by Discord being unreachable.
/// </para>
/// </summary>
public sealed class AlertDispatcher(
    DiscordWebhookSender sender,
    ITrayService tray,
    Func<DiscordConfig> config,
    TimeProvider time,
    ILogger<AlertDispatcher> log,
    ROROROblox.App.Notify.PhoneAlertSender? phoneSender = null,
    Func<ROROROblox.Core.Notify.PhoneNotifyConfig>? phoneConfig = null)
{
    /// <summary>Keyed by (account, KIND) — see <see cref="AlertRouter.Route"/> for why the kind
    /// belongs in the key.</summary>
    private readonly Dictionary<(Guid AccountId, AlertKind Kind), DateTimeOffset> _lastSent = [];

    /// <summary>True once that webhook has returned 404. Surfaced in Settings; also stops the
    /// dispatcher offering the destination at all — see <see cref="EffectiveConfig"/>.</summary>
    public bool MineWebhookRejected { get; private set; }

    public bool ClanWebhookRejected { get; private set; }

    /// <summary>True once the push provider has rejected the saved credentials (a Pushover 4xx
    /// naming the key, an ntfy 403). Same terminal-for-the-session contract as a webhook 404.</summary>
    public bool PhoneRejected { get; private set; }

    /// <summary>
    /// Clear the rejection so a newly pasted webhook gets a real chance. Without this, a user who
    /// deletes a webhook, sees the warning, makes a new one, and pastes it would still be routed
    /// to desktop-only for the rest of the session — punished for fixing the problem.
    /// </summary>
    public void ResetMineRejection() => MineWebhookRejected = false;

    public void ResetClanRejection() => ClanWebhookRejected = false;

    /// <summary>Clear the phone rejection so newly saved credentials get a real chance — the
    /// same fixing-it-must-work rule the webhook resets exist for.</summary>
    public void ResetPhoneRejection() => PhoneRejected = false;

    public async Task DispatchAsync(IReadOnlyList<AlertTrigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        if (triggers.Count == 0) return;

        try
        {
            var current = EffectiveConfig(config());
            var now = time.GetUtcNow();
            var phone = phoneConfig?.Invoke() ?? new ROROROblox.Core.Notify.PhoneNotifyConfig();
            var phoneReady = phoneSender is not null && !PhoneRejected && phone.IsConfigured;
            var routed = AlertRouter.Route(triggers, current, _lastSent, now, phoneReady);

            // Same diagnostic gap that cost three sessions on the presence side: without this, a
            // delivered alert and a swallowed one look identical in the log (both silent). The
            // swallowed cases are the ones worth naming — routed nowhere (the shipped default),
            // muted, or inside the cooldown all look like "the feature is broken" from outside.
            if (routed.Count == 0)
            {
                log.LogInformation(
                    "Alert raised for {Count} account(s) but routed nowhere — check the destination, the per-account mute, and the {Cooldown}-minute cooldown.",
                    triggers.Count, AlertRouter.Cooldown.TotalMinutes);
                return;
            }

            foreach (var alert in routed)
            {
                // The clan channel is the one destination exempt from streamer mode — a room the
                // user deliberately joined, full of people who already know which accounts are
                // theirs. See AlertTrigger for the full reasoning. Every other destination
                // (desktop toast, personal channel) keeps the masked name.
                var payload = WebhookPayload.ForAlert(
                    alert.Kind, alert.Triggers, useRealNames: alert.Destination == AlertDestination.Clan);

                log.LogInformation("Alert → {Destination}: {Title} ({Count} account(s)).",
                    alert.Destination, payload.Title, alert.Triggers.Count);

                switch (alert.Destination)
                {
                    case AlertDestination.Local:
                        tray.ShowToast(payload.Title, payload.Body);
                        break;
                    case AlertDestination.Mine when current.MineWebhookUrl is { } mine:
                        MineWebhookRejected |= await SendAsync(mine, payload).ConfigureAwait(false);
                        break;
                    case AlertDestination.Clan when current.ClanWebhookUrl is { } clan:
                        ClanWebhookRejected |= await SendAsync(clan, payload).ConfigureAwait(false);
                        break;
                    case AlertDestination.Phone when phoneSender is not null:
                        var phoneResult = await phoneSender.SendAsync(phone, alert.Kind, payload).ConfigureAwait(false);
                        if (phoneResult == Notify.PhoneSendResult.EndpointRejected)
                        {
                            log.LogInformation("The push provider rejected the saved credentials; disabling the phone destination.");
                            PhoneRejected = true;
                        }
                        break;
                }

                foreach (var t in alert.Triggers) { _lastSent[(t.AccountId, t.Kind)] = now; }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Alert dispatch failed; the alert was dropped.");
        }
    }

    /// <summary>
    /// Blanks out any webhook already known to be dead, which does two things at once: the dead URL
    /// is never POSTed again, and <see cref="AlertRouter"/>'s existing "destination configured but
    /// no webhook → fall back to the desktop notification" rule takes over automatically, so the
    /// user still finds out.
    /// <para>
    /// Marking a flag is not the same as acting on it. Without this, a 404'd webhook is re-POSTed
    /// on every alert forever AND every one of those alerts vanishes — the worst of both, and the
    /// exact opposite of what "a 404 is terminal" is supposed to buy.
    /// </para>
    /// </summary>
    private DiscordConfig EffectiveConfig(DiscordConfig current) => current with
    {
        MineWebhookUrl = MineWebhookRejected ? null : current.MineWebhookUrl,
        ClanWebhookUrl = ClanWebhookRejected ? null : current.ClanWebhookUrl,
    };

    /// <summary>Returns true when the destination should be treated as dead.</summary>
    private async Task<bool> SendAsync(string url, WebhookPayload payload)
    {
        var result = await sender.SendAsync(url, payload).ConfigureAwait(false);
        if (result == WebhookSendResult.WebhookGone)
        {
            log.LogInformation("A Discord webhook returned 404; disabling that destination.");
            return true;
        }
        return false;
    }
}
