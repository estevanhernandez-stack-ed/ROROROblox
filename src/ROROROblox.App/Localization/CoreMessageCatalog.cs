using ROROROblox.Core;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Notify;

namespace ROROROblox.App.Localization;

/// <summary>
/// The single place the App turns Core's message keys into sentences — the App half of the
/// Core string boundary (docs/superpowers/specs/2026-09-05-core-string-boundary-design.md).
/// Core hands over an enum kind plus data; every sentence a viewer reads for one lives here,
/// and nowhere else, so Phase C of the localization plan converts exactly this file to
/// resource lookups and Core never changes again. Deliberately boring: plain switch
/// expressions preserving the pre-boundary English byte-for-byte.
///
/// House rules carried in from the migrated sites:
/// - A validator message NEVER echoes the rejected paste — these render in Settings and get
///   screenshotted into clan channels, and a mispasted credential or bot token is exactly the
///   thing not worth repeating.
/// - A kind's <c>Detail</c> is diagnostic data (usually an exception's text) — it trails the
///   headline and is not guaranteed to be a sentence or even non-empty.
/// </summary>
internal static class CoreMessageCatalog
{
    public static string For(LaunchResult.Failed failed) => failed.Kind switch
    {
        LaunchFailureKind.NoDefaultGame =>
            "No default Roblox game configured. Add one in Games (header button), or pass an explicit target.",
        LaunchFailureKind.AuthTicketFailed => $"Failed to obtain auth ticket: {failed.Detail}",
        LaunchFailureKind.RobloxNotInstalled => "Roblox does not appear to be installed.",
        LaunchFailureKind.ProcessStartFailed => $"Process.Start failed: {failed.Detail}",
        _ => throw new ArgumentOutOfRangeException(nameof(failed), failed.Kind, null),
    };

    public static string For(CookieCaptureResult.Failed failed) => failed.Kind switch
    {
        CookieCaptureFailureKind.WebView2RuntimeMissing => "WebView2 runtime missing",
        CookieCaptureFailureKind.WebView2InitFailed => $"WebView2 init failed: {failed.Detail}",
        CookieCaptureFailureKind.UserDataDirFailed =>
            $"Cookie capture failed to allocate user-data dir: {failed.Detail}",
        CookieCaptureFailureKind.StartFailed => $"Cookie capture failed to start: {failed.Detail}",
        CookieCaptureFailureKind.CaptureFailed => $"Cookie capture failed: {failed.Detail}",
        CookieCaptureFailureKind.LoginRejected => "Roblox didn't accept the login session.",
        CookieCaptureFailureKind.ProfileFetchFailed => $"Profile fetch failed: {failed.Detail}",
        _ => throw new ArgumentOutOfRangeException(nameof(failed), failed.Kind, null),
    };

    /// <summary>Valid and Empty verdicts render as an empty line — the field speaks for itself.</summary>
    public static string For(WebhookUrlKind kind) => kind switch
    {
        WebhookUrlKind.Valid or WebhookUrlKind.Empty => "",
        WebhookUrlKind.ServerInvite =>
            "That's a server invite. You need a webhook — in Discord: Server Settings → Integrations → Webhooks → New Webhook, then Copy Webhook URL.",
        WebhookUrlKind.ChannelLink =>
            "That's a link to the channel, not a webhook. Same channel, different button: Server Settings → Integrations → Webhooks → New Webhook.",
        WebhookUrlKind.BotToken =>
            "That looks like a bot token — don't share that anywhere, and reset it if you pasted it somewhere public. A webhook URL starts with discord.com/api/webhooks/.",
        WebhookUrlKind.Unrecognized =>
            "That doesn't look like a webhook URL. It should start with discord.com/api/webhooks/ — Server Settings → Integrations → Webhooks → Copy Webhook URL.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// <paramref name="fieldNoun"/> is which Pushover field was inspected ("user key" or
    /// "application token") — the caller's knowledge, passed as data.
    /// </summary>
    public static string ForPushoverKey(PhoneCredentialKind kind, string fieldNoun) => kind switch
    {
        PhoneCredentialKind.Valid or PhoneCredentialKind.Empty => "",
        PhoneCredentialKind.WebhookUrl =>
            $"That's a Discord link — the {fieldNoun} is a 30-character code from pushover.net, not a URL.",
        PhoneCredentialKind.WrongShape =>
            $"That doesn't look like a {fieldNoun} — it's a 30-character code of letters and digits, shown on your pushover.net dashboard.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string ForNtfyServer(PhoneCredentialKind kind) => kind switch
    {
        PhoneCredentialKind.Valid or PhoneCredentialKind.Empty => "",
        PhoneCredentialKind.WebhookUrl or PhoneCredentialKind.WrongShape =>
            "The server needs to be a full address like https://ntfy.sh — leave it as the default unless you run your own.",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// What the History window says about itself (F-038), moved here from Core 2026-09-05.
    /// <para>
    /// THE DEFECT the F-038 prose exists to keep fixed: <c>ReloadAsync</c> caught every read
    /// failure into an empty list, and an empty list renders "No launches yet." — so a history
    /// file that could not be opened presented as a confident statement that the user had never
    /// launched anything. The one screen whose entire job is remembering told them there was
    /// nothing to remember. Clear had the same shape: a clear that did nothing looked exactly
    /// like a clear that worked. Empty and Unreadable get different words because they are
    /// different facts — that difference is the whole finding.
    /// </para>
    /// </summary>
    public static class SessionHistory
    {
        /// <summary>The line at the top of the window: what just happened, in one sentence.</summary>
        public static string StatusLine(SessionHistoryOutcome outcome, int count, string? error) => outcome switch
        {
            SessionHistoryOutcome.Unreadable => $"Couldn't read history{Because(error)}",
            SessionHistoryOutcome.Empty => "Nothing recorded yet.",
            _ => count == 1 ? "1 launch recorded." : $"{count} launches recorded.",
        };

        /// <summary>Shown while the read is in flight. Mirrors Diagnostics' "Collecting…".</summary>
        public const string Loading = "Loading…";

        /// <summary>The centred placeholder that replaces the list.</summary>
        public static (string Headline, string Detail) Placeholder(SessionHistoryOutcome outcome) => outcome switch
        {
            SessionHistoryOutcome.Unreadable => (
                "History couldn't be read.",
                "The file may be open in another program, or damaged. This doesn't affect your saved "
                + "accounts or settings — they're stored separately."),
            _ => (
                "No launches yet.",
                "Click Launch As on any account and you'll see entries here."),
        };

        /// <summary>Said after a Clear that worked. Previously indistinguishable from one that did not.</summary>
        public const string Cleared = "History cleared.";

        /// <summary>Said after a Clear that failed. Names the one thing the user will want to know.</summary>
        public static string ClearFailed(string? error) => $"Couldn't clear history{Because(error)} Nothing was deleted.";

        /// <summary>
        /// Appends the underlying reason when there is one. The message comes from an exception, so
        /// it is not guaranteed to be a sentence or even non-empty — hence the fallback, rather than
        /// producing "Couldn't read history: ." on a stringly-empty IOException.
        /// </summary>
        private static string Because(string? error) =>
            string.IsNullOrWhiteSpace(error) ? "." : $": {error.Trim()}";
    }
}
