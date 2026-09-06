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
/// What the paste field decided. The sentence shown for each kind is the App's
/// (<c>CoreMessageCatalog</c>), per the Core string boundary
/// (docs/superpowers/specs/2026-09-05-core-string-boundary-design.md) — and that sentence
/// must never echo the rejected paste: it renders in Settings and gets screenshotted into a
/// clan channel when someone asks for help, and the paste most worth diagnosing (a bot token)
/// is exactly the one most worth not repeating. (Until 2026-09-05 this record carried the
/// composed <c>Message</c> itself.)
/// </summary>
public sealed record WebhookUrlVerdict(WebhookUrlKind Kind, string? NormalizedUrl);

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
            return new WebhookUrlVerdict(WebhookUrlKind.Empty, null);
        }

        var text = pasted.Trim();

        var match = WebhookRegex().Match(text);
        if (match.Success)
        {
            return new WebhookUrlVerdict(WebhookUrlKind.Valid, match.Value);
        }

        if (text.Contains("discord.gg/", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.ServerInvite, null);
        }

        if (text.Contains("/channels/", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.ChannelLink, null);
        }

        if (BotTokenRegex().IsMatch(text))
        {
            return new WebhookUrlVerdict(WebhookUrlKind.BotToken, null);
        }

        return new WebhookUrlVerdict(WebhookUrlKind.Unrecognized, null);
    }
}
