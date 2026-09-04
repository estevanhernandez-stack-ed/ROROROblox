using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Notify;

namespace ROROROblox.App.Notify;

public enum PhoneSendResult
{
    Sent,

    /// <summary>The credentials themselves were refused (bad key/token, forbidden topic). Terminal
    /// for the session, like a webhook 404 — the caller stops offering the destination and
    /// Settings surfaces it.</summary>
    EndpointRejected,

    RateLimited,
    Failed,
}

/// <summary>
/// Routes a phone alert to whichever provider the user configured. One seam for
/// <c>AlertDispatcher</c> so the provider switch lives here and not in the dispatch loop —
/// adding a third provider later is a new sender plus one arm.
/// </summary>
public sealed class PhoneAlertSender(PushoverSender pushover, NtfySender ntfy, ILogger<PhoneAlertSender> log)
{
    public Task<PhoneSendResult> SendAsync(
        PhoneNotifyConfig config, AlertKind kind, WebhookPayload payload, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(payload);

        if (!config.IsConfigured)
        {
            // The router already falls back to Local for an unconfigured phone; reaching here
            // means config changed between routing and dispatch. Dropping is correct — the
            // desktop toast for this alert already went nowhere, and half a credential must not
            // be sent anywhere.
            log.LogDebug("Phone alert dropped: provider no longer configured at dispatch time.");
            return Task.FromResult(PhoneSendResult.Failed);
        }

        return config.Provider switch
        {
            PhoneProvider.Pushover => pushover.SendAsync(
                config.PushoverUserKey!, config.PushoverAppToken!, kind, payload, ct),
            PhoneProvider.Ntfy => ntfy.SendAsync(
                config.NtfyServerUrl, config.NtfyTopic!, kind, payload, ct),
            _ => Task.FromResult(PhoneSendResult.Failed),
        };
    }
}
