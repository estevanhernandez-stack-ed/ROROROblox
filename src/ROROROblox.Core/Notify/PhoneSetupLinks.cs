namespace ROROROblox.Core.Notify;

/// <summary>
/// The destinations behind the phone-setup QR codes, so a member can point a camera at the
/// screen instead of retyping into a phone keyboard.
///
/// The two providers need opposite handling and the difference is the whole reason this type
/// exists in Core rather than being built inline in the view:
///
/// <list type="bullet">
/// <item><b>ntfy</b> — <see cref="NtfySubscribe"/> embeds the topic, and an ntfy topic IS the
/// security model (see <see cref="NtfyTopicGenerator"/>: it grants subscribe AND publish). The
/// returned string is therefore a bearer credential in URL form. Anything that renders it must
/// treat it exactly like the topic text box — hidden by default, and never on screen while
/// streamer mode is on. A QR is strictly more exposed than the text it replaces: a viewer can
/// scan one off a paused frame, and nobody transcribes 33 base32 characters off a paused
/// frame.</item>
/// <item><b>Pushover</b> — the two links are fixed public pages that carry nothing. They exist
/// because Pushover's credentials flow phone-to-PC while the DESTINATION flows PC-to-phone:
/// the value of a QR here is saving someone from typing a URL on a phone, not moving a secret.
/// They are safe to display openly.</item>
/// </list>
/// </summary>
public static class PhoneSetupLinks
{
    private const string DefaultNtfyHost = "https://ntfy.sh";

    /// <summary>Where the Pushover application token is created. The app can save the RoRoRo
    /// icon for this form's icon slot (<c>SavePushoverIconButton</c>).</summary>
    public const string PushoverApplicationForm = "https://pushover.net/apps/build";

    /// <summary>Where the Pushover user key is shown.</summary>
    public const string PushoverDashboard = "https://pushover.net/";

    /// <summary>
    /// The subscribe URL for <paramref name="topic"/> on <paramref name="server"/>, or
    /// <c>null</c> when there is no topic yet — callers render no QR rather than a code pointing
    /// at a bare server root.
    ///
    /// An https/http URL is used rather than the <c>ntfy://</c> scheme deliberately. The ntfy
    /// apps register for their server's web links, so a scan opens the app already subscribed;
    /// when the app is NOT installed the same scan lands on the topic's web page, which still
    /// receives and still shows the string to copy. The custom scheme has no such fallback — it
    /// fails with nothing to act on.
    /// </summary>
    public static string? NtfySubscribe(string? server, string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return null;

        var host = string.IsNullOrWhiteSpace(server) ? DefaultNtfyHost : server.Trim();

        // A self-hosted ntfy on a LAN is commonly plain http, so an existing scheme is kept as
        // configured. Upgrading it silently would render a QR that cannot connect.
        if (!host.Contains("://", StringComparison.Ordinal)) host = $"https://{host}";

        return $"{host.TrimEnd('/')}/{Uri.EscapeDataString(topic.Trim())}";
    }
}
