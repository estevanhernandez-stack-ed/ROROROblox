using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Notify;

/// <summary>
/// Posts an alert to Pushover (<c>api.pushover.net</c>) — the clan's incumbent push app, per
/// Este 2026-09-04. Accepts only <see cref="WebhookPayload"/>, which by construction cannot
/// carry a private-server link; names arrive already streamer-masked because Phone is not the
/// clan destination.
/// <para>
/// Priority mapping is the point of using Pushover at all: an account DROP is priority 1
/// (bypasses the user's quiet hours — it is the actionable page), a memory warning is 0.
/// </para>
/// <para>
/// Nothing here ever logs the user key or application token — both are bearer credentials, and
/// log files are what users paste into Discord when they ask why alerts stopped. A 4xx naming
/// bad credentials is terminal for the session (<see cref="PhoneSendResult.EndpointRejected"/>);
/// the body is capped under the API's message limit by <see cref="TruncateForPushover"/>,
/// so a 400 means the credentials, not the message. (Before the cap, a mass drop's coalesced
/// body could exceed Pushover's 1024-character limit, whose plain 400 would have latched
/// EndpointRejected against valid keys on exactly the alert that mattered most — review
/// 2026-09-04.)
/// </para>
/// </summary>
/// <remarks>
/// <c>ILogger&lt;PushoverSender&gt;</c>, NOT the non-generic <c>ILogger</c> — the resolve-time
/// crash class guarded by <c>TypedHttpClientRegistrationTests</c>; see
/// <c>DiscordWebhookSender</c>'s remarks for the incident.
/// </remarks>
public sealed class PushoverSender(HttpClient client, ILogger<PushoverSender> log)
{
    private const string Endpoint = "https://api.pushover.net/1/messages.json";

    /// <summary>Pushover's documented message cap.</summary>
    private const int MessageLimit = 1024;

    /// <summary>
    /// Cut an over-long coalesced body at a line break and say how many accounts went unnamed.
    /// One line per dropped account is unbounded (the payload's shape is fixed, its LENGTH is
    /// not), and Pushover answers an over-limit message with the same bare 400 a bad credential
    /// gets — which the caller treats as terminal for the session.
    /// </summary>
    internal static string TruncateForPushover(string body)
    {
        if (body.Length <= MessageLimit) return body;

        var lines = body.Split('\n');
        var kept = new List<string>();
        var length = 0;
        foreach (var line in lines)
        {
            if (length + line.Length + 1 > MessageLimit - 32) break;
            kept.Add(line);
            length += line.Length + 1;
        }

        if (kept.Count == 0)
        {
            // A single monster line; keep the front of it.
            return body[..(MessageLimit - 2)] + "…";
        }

        kept.Add($"…and {lines.Length - kept.Count} more");
        return string.Join("\n", kept);
    }

    public async Task<PhoneSendResult> SendAsync(
        string userKey, string appToken, AlertKind kind, WebhookPayload payload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(appToken);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"] = appToken,
                ["user"] = userKey,
                ["title"] = payload.Title,
                ["message"] = TruncateForPushover(payload.Body),
                ["priority"] = kind == AlertKind.AccountDroppedOut ? "1" : "0",
            });
            using var response = await client.PostAsync(Endpoint, form, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                log.LogInformation("Pushover accepted the alert ({Status}).", (int)response.StatusCode);
                return PhoneSendResult.Sent;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests) return PhoneSendResult.RateLimited;
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                log.LogInformation("Pushover rejected the credentials ({Status}); disabling the phone destination for this session.",
                    (int)response.StatusCode);
                return PhoneSendResult.EndpointRejected;
            }

            log.LogDebug("Pushover post returned {Status}.", (int)response.StatusCode);
            return PhoneSendResult.Failed;
        }
        catch (Exception ex)
        {
            // Type name only: HttpRequestException messages can include the request URI, and the
            // habit of never logging exception objects on credentialed paths is inherited from
            // DiscordWebhookSender even though this endpoint is a constant.
            log.LogDebug("Pushover post failed: {Error}.", ex.GetType().Name);
            return PhoneSendResult.Failed;
        }
    }
}
