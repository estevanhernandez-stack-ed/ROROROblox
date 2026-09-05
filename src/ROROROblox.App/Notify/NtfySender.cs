using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Notify;

/// <summary>
/// Posts an alert to an ntfy topic (default server <c>ntfy.sh</c>, user-overridable for
/// self-hosters). The topic is a bearer credential granting subscribe AND publish, so it gets
/// the webhook-URL treatment: never logged, not even on failure.
/// <para>
/// The notification TITLE is the static string "RoRoRo", not the payload title: ntfy carries the
/// title in an HTTP header, account names can contain characters that are not header-safe, and a
/// name must never be able to break — or inject into — the request envelope. The payload's own
/// title leads the body instead, so the phone shows "RoRoRo — BaronBloxwell dropped out".
/// </para>
/// </summary>
/// <remarks>
/// <c>ILogger&lt;NtfySender&gt;</c>, NOT the non-generic <c>ILogger</c> — the resolve-time crash
/// class guarded by <c>TypedHttpClientRegistrationTests</c>.
/// </remarks>
public sealed class NtfySender(HttpClient client, ILogger<NtfySender> log)
{
    public async Task<PhoneSendResult> SendAsync(
        string serverUrl, string topic, AlertKind kind, WebhookPayload payload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            var url = $"{serverUrl.TrimEnd('/')}/{Uri.EscapeDataString(topic)}";
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent($"{payload.Title}\n{payload.Body}", Encoding.UTF8),
            };
            request.Headers.TryAddWithoutValidation("Title", "RoRoRo");
            request.Headers.TryAddWithoutValidation("Priority",
                kind == AlertKind.AccountDroppedOut ? "high" : "default");

            using var response = await client.SendAsync(request, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                log.LogInformation("ntfy accepted the alert ({Status}).", (int)response.StatusCode);
                return PhoneSendResult.Sent;
            }

            // The free tier's daily cap surfaces as 429. The router's five-minute cooldown keeps
            // normal volume far under it, so a 429 here means something is flapping hard — the
            // cooldown is already the backoff; no retry.
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return PhoneSendResult.RateLimited;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
            {
                log.LogInformation("The ntfy server refused the topic ({Status}); disabling the phone destination for this session.",
                    (int)response.StatusCode);
                return PhoneSendResult.EndpointRejected;
            }

            log.LogDebug("ntfy post returned {Status}.", (int)response.StatusCode);
            return PhoneSendResult.Failed;
        }
        catch (Exception ex)
        {
            // Type name only — an HttpRequestException message can carry the request URI, and the
            // URI here contains the topic, which is the credential.
            log.LogDebug("ntfy post failed: {Error}.", ex.GetType().Name);
            return PhoneSendResult.Failed;
        }
    }
}
