namespace ROROROblox.Core.Discord;

/// <summary>
/// Pure parser: lifts the private-server share URL out of a roblox-player: launch URI when one
/// is present. Returns null for public games and for any malformed input — never throws.
///
/// Format reminder (canonical RobloxLauncher build per v1.0 item 6):
///   roblox-player:1+launchmode:play+gameinfo:TICKET+launchtime:MS+placelauncherurl:URLENCODED+...
///
/// Segments are plus-separated; each segment is "key:value" with the value possibly containing
/// colons (URL-encoded payload). The placelauncherurl value, once Uri.UnescapeDataString'd,
/// is the share-able URL — we recognize private-server payloads by either the legacy
/// "accessCode=" query param or the newer "privateServerLinkCode=" link-share format.
/// </summary>
public static class ServerShareExtractor
{
    private const string Scheme = "roblox-player:";
    private const string Key = "placelauncherurl";

    /// <returns>
    /// The decoded private-server share URL, or null if the URI targets a public game,
    /// has no placelauncherurl segment, or cannot be parsed.
    /// </returns>
    public static string? TryExtractPrivateServerUrl(string launchUri)
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

        // Private-server signatures (case-insensitive — Roblox occasionally re-cases query keys):
        //   accessCode=         — legacy private-server flow + RobloxLauncher AccessCode kind
        //   privateServerLinkCode= — newer share-link format on roblox.com/games/{id}?...
        //   linkCode=           — RobloxLauncher LinkCode kind in the placelauncherurl emit
        //                          (different from the website URL prefix; bug bash 2026-05-06
        //                          surfaced this — Layer 2 outbound was missing every LinkCode
        //                          launch because we were only checking the website prefix)
        if (decoded.Contains("accessCode=", StringComparison.OrdinalIgnoreCase) ||
            decoded.Contains("privateServerLinkCode=", StringComparison.OrdinalIgnoreCase) ||
            decoded.Contains("linkCode=", StringComparison.OrdinalIgnoreCase))
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
