namespace ROROROblox.Core.Notify;

/// <summary>
/// The ntfy subscribe link behind the phone-setup QR code, so nobody retypes a 33-character
/// topic into a phone keyboard.
///
/// This lives in Core rather than inline in the view because of what the string IS:
/// <see cref="NtfySubscribe"/> embeds the topic, and an ntfy topic is the whole security model
/// (see <see cref="NtfyTopicGenerator"/>: it grants subscribe AND publish). The returned string
/// is a bearer credential in URL form. Anything that renders it must keep it hidden by default
/// and off screen entirely while streamer mode is on. A QR is strictly MORE exposed than the
/// text it replaces: a viewer can scan one off a single paused frame, and nobody transcribes 33
/// base32 characters off a paused frame.
///
/// DO NOT read that as a description of the topic text box (checked 2026-09-12). It is a
/// requirement this class's renderer meets and the text box does not. A revealed topic survives
/// a streamer-mode flip and stays on screen, and so do both Discord webhook URLs and the
/// Pushover user key and app token. <c>StreamerModeToggle</c> is read in exactly ONE place in
/// the whole Settings page -- the QR refresh this type feeds. The webhook URLs are the sharper
/// end of that gap, being short enough to read off a paused frame and granting post rights to
/// the channel. Tracked separately from the QR work that found it.
///
/// WHY THERE IS NO PUSHOVER LINK HERE (2026-09-12). Two Pushover codes shipped in the first
/// draft of this class, pointing at the dashboard and the application form. They were dropped
/// after a live scan test, for three reasons found in one sitting:
/// <list type="bullet">
/// <item>They point the wrong way. Pushover setup is a PC flow start to finish -- the
/// application is created in a browser, and RoRoRo's own "Save the RoRoRo icon" button saves
/// that icon TO THE PC for upload on the same page. Sending the user to their phone is work
/// added, not saved.</item>
/// <item>The dashboard link lands badly. The user key is on <c>pushover.net</c>, but the
/// mobile layout leads with a send-a-notification form, so the page reads as the wrong
/// one.</item>
/// <item>Two codes side by side cannot be scanned individually. A camera sees both, and each
/// had to shrink to 118px to fit where the ntfy code gets 176px alone.</item>
/// </list>
/// If a Pushover code is ever wanted again, the honest one is a link to the app store: installing
/// the app is the only step in that flow that actually happens on the phone.
/// </summary>
public static class PhoneSetupLinks
{
    private const string DefaultNtfyHost = "https://ntfy.sh";

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
