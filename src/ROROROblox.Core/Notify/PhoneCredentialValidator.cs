using System.Text.RegularExpressions;

namespace ROROROblox.Core.Notify;

public enum PhoneCredentialKind
{
    Valid,
    Empty,
    WebhookUrl,
    WrongShape,
}

/// <summary>
/// What the paste field decided, and what to tell the user. Same contract as
/// <c>WebhookUrlValidator</c>: <see cref="Message"/> NEVER echoes the rejected paste — these
/// strings render in Settings and get screenshotted into clan channels when someone asks for
/// help, and a mispasted credential is exactly the thing not worth repeating.
/// </summary>
public sealed record PhoneCredentialVerdict(PhoneCredentialKind Kind, string? Normalized, string Message);

/// <summary>
/// Names what the user actually pasted into the Pushover fields. The two shapes people get
/// wrong: pasting a Discord webhook (the field right above these taught them that habit), and
/// pasting an e-mail/login instead of the 30-char key. "Invalid" teaches nothing; naming the
/// mistake does.
/// </summary>
public static partial class PhoneCredentialValidator
{
    [GeneratedRegex("^[A-Za-z0-9]{30}$")]
    private static partial Regex PushoverKeyRegex();

    [GeneratedRegex(@"https://(?:\w+\.)?discord(?:app)?\.com/", RegexOptions.IgnoreCase)]
    private static partial Regex DiscordUrlRegex();

    public static PhoneCredentialVerdict InspectPushoverKey(string? pasted, string fieldNoun)
    {
        if (string.IsNullOrWhiteSpace(pasted))
        {
            return new PhoneCredentialVerdict(PhoneCredentialKind.Empty, null, "");
        }

        var text = pasted.Trim();

        if (DiscordUrlRegex().IsMatch(text))
        {
            return new PhoneCredentialVerdict(PhoneCredentialKind.WebhookUrl, null,
                $"That's a Discord link — the {fieldNoun} is a 30-character code from pushover.net, not a URL.");
        }

        if (PushoverKeyRegex().IsMatch(text))
        {
            return new PhoneCredentialVerdict(PhoneCredentialKind.Valid, text, "");
        }

        return new PhoneCredentialVerdict(PhoneCredentialKind.WrongShape, null,
            $"That doesn't look like a {fieldNoun} — it's a 30-character code of letters and digits, shown on your pushover.net dashboard.");
    }

    /// <summary>ntfy server override: must be an absolute https URL (or http for a LAN self-host).</summary>
    public static PhoneCredentialVerdict InspectNtfyServer(string? pasted)
    {
        if (string.IsNullOrWhiteSpace(pasted))
        {
            return new PhoneCredentialVerdict(PhoneCredentialKind.Empty, null, "");
        }

        var text = pasted.Trim().TrimEnd('/');
        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
               && uri.Scheme is "https" or "http"
            ? new PhoneCredentialVerdict(PhoneCredentialKind.Valid, text, "")
            : new PhoneCredentialVerdict(PhoneCredentialKind.WrongShape, null,
                "The server needs to be a full address like https://ntfy.sh — leave it as the default unless you run your own.");
    }
}
