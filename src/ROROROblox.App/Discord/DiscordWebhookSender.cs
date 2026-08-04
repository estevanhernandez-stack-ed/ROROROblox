using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

public enum WebhookSendResult
{
    Sent,
    WebhookGone,
    RateLimited,
    Failed,
}

/// <summary>
/// Posts an alert to a Discord webhook. Accepts only <see cref="WebhookPayload"/>, which by
/// construction cannot carry a private-server link.
/// <para>
/// A 404 is terminal — a deleted webhook does not come back, and the caller disables that
/// destination rather than retrying forever.
/// </para>
/// <para>
/// Nothing here ever logs the webhook URL. It is a bearer credential: anyone holding it can post
/// to that channel indefinitely, and log files are what users paste into Discord when they ask why
/// alerts stopped arriving. The status code is the whole diagnostic value; the token is not.
/// </para>
/// </summary>
public sealed class DiscordWebhookSender(HttpClient client, ILogger log)
{
    public async Task<WebhookSendResult> SendAsync(string url, WebhookPayload payload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            var body = new { content = $"**{payload.Title}**\n{payload.Body}" };
            using var response = await client.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode) return WebhookSendResult.Sent;
            if (response.StatusCode == HttpStatusCode.NotFound) return WebhookSendResult.WebhookGone;
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return WebhookSendResult.RateLimited;

            log.LogDebug("Webhook post returned {Status}.", (int)response.StatusCode);
            return WebhookSendResult.Failed;
        }
        catch (Exception ex)
        {
            // No URL, and no exception object either: HttpRequestException messages routinely
            // include the request URI, which would put the token in the log by the back door.
            log.LogDebug("Webhook post failed: {Error}.", ex.GetType().Name);
            return WebhookSendResult.Failed;
        }
    }
}
