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
/// <remarks>
/// <c>ILogger&lt;DiscordWebhookSender&gt;</c>, NOT the non-generic <c>ILogger</c>: DI registers only
/// the generic form, so a non-generic parameter fails at RESOLVE time with "Unable to resolve
/// service for type 'Microsoft.Extensions.Logging.ILogger'". That is invisible to a clean build and
/// to every direct-construction unit test — it shipped to a smoke build here, where it took down
/// AlertDispatcher and, through it, the whole Preferences window. Guarded by
/// <c>TypedHttpClientRegistrationTests</c>.
/// </remarks>
public sealed class DiscordWebhookSender(HttpClient client, ILogger<DiscordWebhookSender> log)
{
    public async Task<WebhookSendResult> SendAsync(string url, WebhookPayload payload, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            var body = new { content = $"**{payload.Title}**\n{payload.Body}" };
            using var response = await client.PostAsJsonAsync(url, body, ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                log.LogInformation("Webhook post accepted ({Status}).", (int)response.StatusCode);
                return WebhookSendResult.Sent;
            }

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
