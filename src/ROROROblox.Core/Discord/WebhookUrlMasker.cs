namespace ROROROblox.Core.Discord;

/// <summary>
/// Renders a saved webhook URL as something safe to have on screen.
/// <para>
/// A Discord webhook URL is a bearer credential: the token segment IS the whole authentication, and
/// anyone holding it can post to that channel until it is deleted. RoRoRo ships a streamer mode
/// whose entire job is keeping identity off a live stream, while Settings put a working credential
/// on screen at full contrast. This was found the blunt way — a scripted screenshot during the
/// 2026-08-04 UI campaign captured a real one into a PNG (F-076).
/// </para>
/// <para>
/// The mask keeps enough to answer "is the right one saved, and which of my two is this?" —
/// scheme, host, and the last few characters — and nothing an onlooker could post with.
/// </para>
/// </summary>
public static class WebhookUrlMasker
{
    /// <summary>How many trailing characters survive. Enough to tell two webhooks apart, far too
    /// few to reconstruct a ~68-character token.</summary>
    private const int VisibleTail = 5;

    private const string Ellipsis = "…";

    /// <summary>
    /// Masks <paramref name="url"/> for display. Returns an empty string for null/blank input, so a
    /// caller can bind the result straight to a TextBox with no branch.
    /// </summary>
    public static string Mask(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";

        var trimmed = url.Trim();

        // Anything that does not parse is treated as a secret in full. A string that reached this
        // method is a stored credential whatever its shape, and revealing an unrecognised one
        // because it failed a format check would be the wrong way round.
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return Ellipsis + Tail(trimmed);
        }

        // Path is dropped entirely rather than partly kept: it carries the webhook id as well as the
        // token, and the id names the channel even though it cannot post on its own.
        var prefix = $"{uri.Scheme}://{uri.Host}/";

        return prefix + Ellipsis + Tail(trimmed);
    }

    /// <summary>
    /// True when <paramref name="text"/> is a mask this class produced rather than a real URL.
    /// Lets a commit path recognise "the user never revealed or edited this" without tracking a
    /// separate flag that could drift out of sync with what is on screen.
    /// </summary>
    public static bool IsMasked(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains(Ellipsis, StringComparison.Ordinal);

    private static string Tail(string value)
    {
        // A value short enough that the tail would be most of it gets no tail at all — better to
        // show the user nothing than to show them most of a short secret.
        if (value.Length <= VisibleTail * 2) return "";
        return value[^VisibleTail..];
    }
}
