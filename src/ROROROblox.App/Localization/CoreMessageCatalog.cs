using ROROROblox.Core;
using ROROROblox.Core.Discord;
using ROROROblox.Core.Notify;

namespace ROROROblox.App.Localization;

/// <summary>
/// The single place the App turns Core's message kinds into sentences — the App half of the Core
/// string boundary (docs/superpowers/specs/2026-09-05-core-string-boundary-design.md). Core hands
/// over an enum kind plus data; every sentence a viewer reads for one is a resx key resolved here.
///
/// <para>Localization Phase D (2026-09-07): this file no longer holds English literals — each kind
/// maps to a <c>CoreMsg_*</c> resx key resolved through <see cref="Loc"/> (live-toggle aware).
/// English values live in Strings.resx; translations arrive in the step-4 pass, and until then
/// non-English falls back to English for these keys. Core never changes.</para>
///
/// House rules carried in from the migrated sites:
/// - A validator message NEVER echoes the rejected paste — these render in Settings and get
///   screenshotted into clan channels, and a mispasted credential or bot token is exactly the
///   thing not worth repeating.
/// - A kind's <c>Detail</c> is diagnostic data (usually an exception's text) — it trails the
///   headline via <c>{0}</c> and is not guaranteed to be a sentence or even non-empty.
/// </summary>
internal static class CoreMessageCatalog
{
    public static string For(LaunchResult.Failed failed) => failed.Kind switch
    {
        LaunchFailureKind.NoDefaultGame => Loc.Get("CoreMsg_Launch_NoDefaultGame"),
        LaunchFailureKind.AuthTicketFailed => Loc.Format("CoreMsg_Launch_AuthTicketFailed", failed.Detail),
        LaunchFailureKind.RobloxNotInstalled => Loc.Get("CoreMsg_Launch_RobloxNotInstalled"),
        LaunchFailureKind.ProcessStartFailed => Loc.Format("CoreMsg_Launch_ProcessStartFailed", failed.Detail),
        _ => throw new ArgumentOutOfRangeException(nameof(failed), failed.Kind, null),
    };

    public static string For(CookieCaptureResult.Failed failed) => failed.Kind switch
    {
        CookieCaptureFailureKind.WebView2RuntimeMissing => Loc.Get("CoreMsg_Cookie_WebView2RuntimeMissing"),
        CookieCaptureFailureKind.WebView2InitFailed => Loc.Format("CoreMsg_Cookie_WebView2InitFailed", failed.Detail),
        CookieCaptureFailureKind.UserDataDirFailed => Loc.Format("CoreMsg_Cookie_UserDataDirFailed", failed.Detail),
        CookieCaptureFailureKind.StartFailed => Loc.Format("CoreMsg_Cookie_StartFailed", failed.Detail),
        CookieCaptureFailureKind.CaptureFailed => Loc.Format("CoreMsg_Cookie_CaptureFailed", failed.Detail),
        CookieCaptureFailureKind.LoginRejected => Loc.Get("CoreMsg_Cookie_LoginRejected"),
        CookieCaptureFailureKind.ProfileFetchFailed => Loc.Format("CoreMsg_Cookie_ProfileFetchFailed", failed.Detail),
        _ => throw new ArgumentOutOfRangeException(nameof(failed), failed.Kind, null),
    };

    /// <summary>Valid and Empty verdicts render as an empty line — the field speaks for itself.</summary>
    public static string For(WebhookUrlKind kind) => kind switch
    {
        WebhookUrlKind.Valid or WebhookUrlKind.Empty => "",
        WebhookUrlKind.ServerInvite => Loc.Get("CoreMsg_Webhook_ServerInvite"),
        WebhookUrlKind.ChannelLink => Loc.Get("CoreMsg_Webhook_ChannelLink"),
        WebhookUrlKind.BotToken => Loc.Get("CoreMsg_Webhook_BotToken"),
        WebhookUrlKind.Unrecognized => Loc.Get("CoreMsg_Webhook_Unrecognized"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// <paramref name="fieldNoun"/> is which Pushover field was inspected ("user key" or
    /// "application token") — the caller's knowledge, passed as data.
    /// </summary>
    public static string ForPushoverKey(PhoneCredentialKind kind, string fieldNoun) => kind switch
    {
        PhoneCredentialKind.Valid or PhoneCredentialKind.Empty => "",
        PhoneCredentialKind.WebhookUrl => Loc.Format("CoreMsg_Pushover_WebhookUrl", fieldNoun),
        PhoneCredentialKind.WrongShape => Loc.Format("CoreMsg_Pushover_WrongShape", fieldNoun),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string ForNtfyServer(PhoneCredentialKind kind) => kind switch
    {
        PhoneCredentialKind.Valid or PhoneCredentialKind.Empty => "",
        PhoneCredentialKind.WebhookUrl or PhoneCredentialKind.WrongShape => Loc.Get("CoreMsg_Ntfy_Server"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// What the History window says about itself (F-038), moved to the App 2026-09-05 and made
    /// resource-backed 2026-09-07.
    /// <para>
    /// THE DEFECT the F-038 prose exists to keep fixed: <c>ReloadAsync</c> caught every read
    /// failure into an empty list, and an empty list renders "No launches yet." — so a history
    /// file that could not be opened presented as a confident statement that the user had never
    /// launched anything. Empty and Unreadable get different words because they are different facts.
    /// </para>
    /// </summary>
    public static class SessionHistory
    {
        /// <summary>The line at the top of the window: what just happened, in one sentence.</summary>
        public static string StatusLine(SessionHistoryOutcome outcome, int count, string? error) => outcome switch
        {
            SessionHistoryOutcome.Unreadable => Loc.Format("CoreMsg_History_Unreadable", Because(error)),
            SessionHistoryOutcome.Empty => Loc.Get("CoreMsg_History_Empty"),
            _ => Loc.Plural("CoreMsg_History_Recorded", count),
        };

        /// <summary>Shown while the read is in flight. Mirrors Diagnostics' "Collecting…".</summary>
        public static string Loading => Loc.Get("CoreMsg_History_Loading");

        /// <summary>The centred placeholder that replaces the list.</summary>
        public static (string Headline, string Detail) Placeholder(SessionHistoryOutcome outcome) => outcome switch
        {
            SessionHistoryOutcome.Unreadable => (
                Loc.Get("CoreMsg_History_PlaceholderUnreadableHeadline"),
                Loc.Get("CoreMsg_History_PlaceholderUnreadableDetail")),
            _ => (
                Loc.Get("CoreMsg_History_PlaceholderEmptyHeadline"),
                Loc.Get("CoreMsg_History_PlaceholderEmptyDetail")),
        };

        /// <summary>Said after a Clear that worked. Previously indistinguishable from one that did not.</summary>
        public static string Cleared => Loc.Get("CoreMsg_History_Cleared");

        /// <summary>Said after a Clear that failed. Names the one thing the user will want to know.</summary>
        public static string ClearFailed(string? error) => Loc.Format("CoreMsg_History_ClearFailed", Because(error));

        /// <summary>
        /// Appends the underlying reason when there is one. The message comes from an exception, so
        /// it is not guaranteed to be a sentence or even non-empty — hence the fallback, rather than
        /// producing "Couldn't read history: ." on a stringly-empty IOException. Punctuation only, so
        /// it stays in code; the localized template carries the "{0}" slot.
        /// </summary>
        private static string Because(string? error) =>
            string.IsNullOrWhiteSpace(error) ? "." : $": {error.Trim()}";
    }
}
