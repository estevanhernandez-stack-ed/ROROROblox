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
/// What the paste field decided. The sentence for each kind is the App's
/// (<c>CoreMessageCatalog</c> — which field's noun to use is the caller's knowledge, so the
/// catalog has one formatter per field), per the Core string boundary
/// (docs/superpowers/specs/2026-09-05-core-string-boundary-design.md). The contract inherited
/// from <c>WebhookUrlValidator</c> stands: the shown sentence NEVER echoes the rejected paste —
/// it renders in Settings and gets screenshotted into clan channels when someone asks for help,
/// and a mispasted credential is exactly the thing not worth repeating. (Until 2026-09-05 this
/// record carried the composed <c>Message</c> itself, which is why the validator also used to
/// take a <c>fieldNoun</c> the validation never needed.)
/// </summary>
public sealed record PhoneCredentialVerdict(PhoneCredentialKind Kind, string? Normalized);

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

    public static PhoneCredentialVerdict InspectPushoverKey(string? pasted)
    {
        if (string.IsNullOrWhiteSpace(pasted))
        {
            return new PhoneCredentialVerdict(PhoneCredentialKind.Empty, null);
        }

        var text = pasted.Trim();

        if (DiscordUrlRegex().IsMatch(text))
        {
            return new PhoneCredentialVerdict(PhoneCredentialKind.WebhookUrl, null);
        }

        if (PushoverKeyRegex().IsMatch(text))
        {
            return new PhoneCredentialVerdict(PhoneCredentialKind.Valid, text);
        }

        return new PhoneCredentialVerdict(PhoneCredentialKind.WrongShape, null);
    }

    /// <summary>ntfy server override: must be an absolute https URL (or http for a LAN self-host).</summary>
    public static PhoneCredentialVerdict InspectNtfyServer(string? pasted)
    {
        if (string.IsNullOrWhiteSpace(pasted))
        {
            return new PhoneCredentialVerdict(PhoneCredentialKind.Empty, null);
        }

        var text = pasted.Trim().TrimEnd('/');
        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
               && uri.Scheme is "https" or "http"
            ? new PhoneCredentialVerdict(PhoneCredentialKind.Valid, text)
            : new PhoneCredentialVerdict(PhoneCredentialKind.WrongShape, null);
    }
}
