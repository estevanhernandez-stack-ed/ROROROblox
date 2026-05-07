namespace ROROROblox.Core.Discord;

/// <summary>
/// Pure parser: lifts a shareable Roblox URL out of a roblox-player: launch URI when one is
/// present. Returns the URL for any Roblox launch (public game OR private server), null only
/// when the URI carries no recognizable place reference (FollowFriend, malformed, etc).
/// Never throws.
///
/// Format reminder (canonical RobloxLauncher build per v1.0 item 6):
///   roblox-player:1+launchmode:play+gameinfo:TICKET+launchtime:MS+placelauncherurl:URLENCODED+...
///
/// v1.2 spec originally constrained this to private-server-only; CHECKPOINT 2 smoke surfaced
/// that public-game Join is also useful for clan coordination ("come find me" — same place,
/// possibly different shard, but Roblox's friend-join can land them together). Expanded to
/// match any URL with placeId= as the fallback. The narrower private-server signals
/// (accessCode= / linkCode= / privateServerLinkCode=) are checked first so the recognized
/// shape stays attributable in tests + diagnostics.
/// </summary>
public static class ServerShareExtractor
{
    private const string Scheme = "roblox-player:";
    private const string Key = "placelauncherurl";

    /// <returns>
    /// The decoded shareable URL — private-server share URL when one is present, otherwise
    /// the public-game URL with placeId. Null if the URI carries no placelauncherurl segment
    /// or none of the recognized share/place params. Never throws.
    /// </returns>
    public static string? TryExtractShareableUrl(string launchUri)
    {
        if (string.IsNullOrWhiteSpace(launchUri))
        {
            return null;
        }

        var body = StripScheme(launchUri);
        if (body.Length == 0)
        {
            return null;
        }

        string? encoded = null;
        foreach (var segment in body.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            // Split on the FIRST colon only — the value (URL-encoded) may itself contain colons
            // after decoding, but we never see a raw decoded colon here.
            var colon = segment.IndexOf(':');
            if (colon <= 0 || colon == segment.Length - 1)
            {
                continue;
            }
            var key = segment[..colon];
            if (key.Equals(Key, StringComparison.OrdinalIgnoreCase))
            {
                encoded = segment[(colon + 1)..];
                break;
            }
        }

        if (string.IsNullOrEmpty(encoded))
        {
            return null;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(encoded);
        }
        catch (UriFormatException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Malformed escape sequence in input — pre-.NET 8 throws ArgumentException;
            // newer versions throw UriFormatException. Cover both.
            return null;
        }

        // Recognized share / place signatures, all case-insensitive.
        // Private-server-specific (narrow signal):
        //   accessCode=             — legacy private-server flow + RobloxLauncher AccessCode kind
        //   privateServerLinkCode=  — newer share-link format on roblox.com/games/{id}?...
        //   linkCode=               — RobloxLauncher LinkCode kind in the placelauncherurl emit
        // Public-game fallback (broad signal — v1.2.5 expansion):
        //   placeId=                — every Roblox launch URL carries placeId; if no narrower
        //                             signal matched, this still gives the joiner enough info
        //                             to land in the same place (same-shard not guaranteed,
        //                             but the place is enough for clan-side coordination).
        if (decoded.Contains("accessCode=", StringComparison.OrdinalIgnoreCase) ||
            decoded.Contains("privateServerLinkCode=", StringComparison.OrdinalIgnoreCase) ||
            decoded.Contains("linkCode=", StringComparison.OrdinalIgnoreCase) ||
            decoded.Contains("placeId=", StringComparison.OrdinalIgnoreCase))
        {
            return decoded;
        }

        return null;
    }

    private static string StripScheme(string launchUri)
    {
        if (launchUri.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return launchUri[Scheme.Length..];
        }
        return launchUri;
    }
}
