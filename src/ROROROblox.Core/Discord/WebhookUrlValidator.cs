using System.Text.RegularExpressions;

namespace ROROROblox.Core.Discord;

public enum WebhookUrlKind
{
    Valid,
    Empty,
    ServerInvite,
    ChannelLink,
    BotToken,
    Unrecognized,
}

/// <summary>
/// What the paste field decided, and what to tell the user about it. <see cref="Message"/> never
/// echoes the rejected paste — this string renders in Settings and gets screenshotted into a clan
/// channel when someone asks for help, and the paste most worth diagnosing (a bot token) is
/// exactly the one most worth not repeating.
/// </summary>
public sealed record WebhookUrlVerdict(WebhookUrlKind Kind, string? NormalizedUrl, string Message);

/// <summary>
/// Names what the user actually pasted. Nobody gets a webhook URL right the first time, and
/// "invalid URL" teaches them nothing — the four wrong things people paste are each recognisable,
/// so each gets told what it is and where the real one lives.
/// </summary>
public static partial class WebhookUrlValidator
{
    [GeneratedRegex(@"https://(?:\w+\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w\-]+", RegexOptions.IgnoreCase)]
    private static partial Regex WebhookRegex();

    [GeneratedRegex(@"^[\w\-]{20,}\.[\w\-]{5,}\.[\w\-]{20,}$")]
    private static partial Regex BotTokenRegex();

    public static WebhookUrlVerdict Inspect(string? pasted)
    {
        if (string.IsNullOrWhiteSpace(pasted))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.Empty, null, "");
        }

        var text = pasted.Trim();

        var match = WebhookRegex().Match(text);
        if (match.Success)
        {
            return new WebhookUrlVerdict(WebhookUrlKind.Valid, match.Value, "");
        }

        if (text.Contains("discord.gg/", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.ServerInvite, null,
                "That's a server invite. You need a webhook — in Discord: Server Settings → Integrations → Webhooks → New Webhook, then Copy Webhook URL.");
        }

        if (text.Contains("/channels/", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.ChannelLink, null,
                "That's a link to the channel, not a webhook. Same channel, different button: Server Settings → Integrations → Webhooks → New Webhook.");
        }

        if (BotTokenRegex().IsMatch(text))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.BotToken, null,
                "That looks like a bot token — don't share that anywhere, and reset it if you pasted it somewhere public. A webhook URL starts with discord.com/api/webhooks/.");
        }

        return new WebhookUrlVerdict(WebhookUrlKind.Unrecognized, null,
            "That doesn't look like a webhook URL. It should start with discord.com/api/webhooks/ — Server Settings → Integrations → Webhooks → Copy Webhook URL.");
    }
}
