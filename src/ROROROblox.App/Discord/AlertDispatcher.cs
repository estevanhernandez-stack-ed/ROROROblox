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
    ILogger log)
{
    private readonly Dictionary<Guid, DateTimeOffset> _lastSent = [];

    /// <summary>True once that webhook has returned 404. Surfaced in Settings; also stops the
    /// dispatcher offering the destination at all — see <see cref="EffectiveConfig"/>.</summary>
    public bool MineWebhookRejected { get; private set; }

    public bool ClanWebhookRejected { get; private set; }

    public async Task DispatchAsync(IReadOnlyList<AlertTrigger> triggers)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        if (triggers.Count == 0) return;

        try
        {
            var current = EffectiveConfig(config());
            var now = time.GetUtcNow();
            var routed = AlertRouter.Route(triggers, current, _lastSent, now);

            foreach (var alert in routed)
            {
                var payload = WebhookPayload.ForAlert(alert.Kind, alert.Triggers);

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
                }

                foreach (var t in alert.Triggers) { _lastSent[t.AccountId] = now; }
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
