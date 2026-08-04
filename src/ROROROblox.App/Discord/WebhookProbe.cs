using System.Net.Http;
using System.Text.Json;

namespace ROROROblox.App.Discord;

/// <summary>Where a webhook actually posts.</summary>
public sealed record WebhookIdentity(string ChannelName, string GuildName);

/// <summary>
/// GETs a webhook to find out which channel and server it belongs to, so Settings can show
/// "Posts to #rororo-alerts in Este's Server" before the first alert is sent. Catching a clan
/// webhook pasted into the personal slot at setup time is the whole point — the alternative is
/// finding out when something private lands in a channel forty people read.
/// <para>
/// Best-effort by contract: every failure returns null. Setup help must never block the user, and
/// "we could not reach Discord to look this up" is not the same as "this webhook is broken."
/// </para>
/// </summary>
public sealed class WebhookProbe(HttpClient client)
{
    public async Task<WebhookIdentity?> DescribeAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            using var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            var channel = ReadName(doc.RootElement, "source_channel");
            var guild = ReadName(doc.RootElement, "source_guild");

            return channel is null && guild is null
                ? null
                : new WebhookIdentity(channel ?? "unknown", guild ?? "your server");
        }
        catch
        {
            return null;   // setup help is best-effort; never block the user on it
        }
    }

    private static string? ReadName(JsonElement root, string property) =>
        root.TryGetProperty(property, out var node) && node.TryGetProperty("name", out var name)
            ? name.GetString()
            : null;
}
