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
            // Unreachable from today's two callers — the dispatcher snapshots ONE immutable
            // record and hands the same instance to routing and to this call, and the Settings
            // test button pre-checks IsConfigured on the snapshot it passes (review 2026-09-04
            // corrected an earlier comment that claimed a routing/dispatch race exists; it does
            // not). Kept as a belt for future callers: half a credential must never be sent
            // anywhere, whoever forgets the pre-check.
            log.LogDebug("Phone alert dropped: the config handed in was not fully configured.");
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
