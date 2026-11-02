using System.Globalization;
using ROROROblox.Core;

namespace ROROROblox.App.Games;

/// <summary>What the Library's one box decided a piece of text is.</summary>
internal enum LibraryPasteKind
{
    /// <summary>Plain text with no link shape: a search term, not a paste.</summary>
    NotAPaste,

    /// <summary>A game: a Roblox game URL, a launcher URL for a place, or a bare place id.</summary>
    Place,

    /// <summary>A private-server link, resolved locally (<c>privateServerLinkCode</c> / <c>accessCode</c>) or a <c>roblox.com/share</c> token that needs the resolve delegate.</summary>
    PrivateServer,

    /// <summary>Link-shaped but not a Roblox link we will act on: another host, another scheme, a <c>javascript:</c> string. Adds nothing, launches nothing.</summary>
    Rejected,
}

/// <summary>The verdict, with the place id for <see cref="LibraryPasteKind.Place"/>.</summary>
internal readonly record struct LibraryPasteVerdict(LibraryPasteKind Kind, long PlaceId = 0)
{
    public static readonly LibraryPasteVerdict NotAPaste = new(LibraryPasteKind.NotAPaste);
    public static readonly LibraryPasteVerdict Rejected = new(LibraryPasteKind.Rejected);
}

/// <summary>
/// The gate in front of the existing parsers. <see cref="LaunchTarget.FromUrl"/>,
/// <see cref="LaunchTarget.TryParseShareLink"/> and <see cref="RobloxLauncher.ExtractPlaceId"/> are
/// substring matchers (<c>placeId=</c>, <c>roblox.com/games/</c>) that never look at the host, so
/// <c>https://evil.example/?placeId=5</c> and <c>javascript:alert(1)//placeId=5</c> parse as place 5.
/// This does not parse a URL for meaning; it decides whether the text may reach those parsers at all:
/// a link-shaped string must be http(s) on <c>roblox.com</c> or a subdomain, and anything else is
/// <see cref="LibraryPasteKind.Rejected"/>. A bare digit string is a place id.
/// </summary>
internal static class LibraryPaste
{
    public static LibraryPasteVerdict Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LibraryPasteVerdict.NotAPaste;
        }

        var trimmed = text.Trim();

        if (long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var bare) && bare > 0)
        {
            return new LibraryPasteVerdict(LibraryPasteKind.Place, bare);
        }

        if (!LooksLikeALink(trimmed))
        {
            return LibraryPasteVerdict.NotAPaste;
        }

        if (!IsRobloxWebUrl(trimmed))
        {
            return LibraryPasteVerdict.Rejected;
        }

        var target = LaunchTarget.FromUrl(trimmed);
        switch (target)
        {
            case LaunchTarget.PrivateServer:
                return new LibraryPasteVerdict(LibraryPasteKind.PrivateServer);
            case LaunchTarget.Place place:
                return new LibraryPasteVerdict(LibraryPasteKind.Place, RobloxLauncher.ExtractPlaceId(trimmed) ?? place.PlaceId);
        }

        // The newer roblox.com/share?code=X&type=Server token: opaque until Roblox resolves it.
        if (LaunchTarget.TryParseShareLink(trimmed, out _, out var linkType)
            && string.Equals(linkType, "Server", StringComparison.OrdinalIgnoreCase))
        {
            return new LibraryPasteVerdict(LibraryPasteKind.PrivateServer);
        }

        return LibraryPasteVerdict.Rejected;
    }

    /// <summary>
    /// Link-shaped means: has a scheme (a colon before any space or slash), a double slash, the word
    /// roblox.com, a www prefix, or a placeId query. Words and sentences are not.
    /// </summary>
    private static bool LooksLikeALink(string s)
    {
        if (s.Contains("://", StringComparison.Ordinal)
            || s.Contains("roblox.com", StringComparison.OrdinalIgnoreCase)
            || s.Contains("placeId=", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            || s.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        var colon = s.IndexOf(':');
        if (colon > 0 && char.IsLetter(s[0]))
        {
            // "javascript:alert(1)", "data:text/html,...", "mailto:x" have a scheme. "Adopt Me: new" has a space first.
            var stop = s.IndexOfAny([' ', '\t', '/']);
            return stop < 0 || stop > colon;
        }

        return false;
    }

    private static bool IsRobloxWebUrl(string s)
    {
        var candidate = s;
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            // Scheme-less paste (roblox.com/games/123, www.roblox.com/...): add one so Uri can read the host.
            // Anything else without a double slash (javascript:, data:, a leading //host) is rejected.
            if (!(candidate.StartsWith("roblox.com", StringComparison.OrdinalIgnoreCase)
                  || candidate.StartsWith("www.roblox.com", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.IdnHost;
        return host.Equals("roblox.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".roblox.com", StringComparison.OrdinalIgnoreCase);
    }
}
