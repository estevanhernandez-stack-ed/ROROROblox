namespace ROROROblox.Core.Discord;

/// <summary>
/// The length envelope one destination takes, in UTF-16 units: the longest title and the longest
/// body <see cref="WebhookPayload.ForAlert"/> may hand it. Built per routed destination, so each
/// gets as many account lines as ITS envelope holds, with "and N more" for the rest (2026-09-15).
/// </summary>
public readonly record struct PayloadLimits(int Title, int Body)
{
    /// <summary>
    /// Discord, Pushover and ntfy. See <see cref="WebhookPayload.TitleLimit"/> and
    /// <see cref="WebhookPayload.BodyLimit"/> for why one envelope fits all three.
    /// </summary>
    public static readonly PayloadLimits Remote = new(WebhookPayload.TitleLimit, WebhookPayload.BodyLimit);

    /// <summary>
    /// The desktop toast. <c>TrayService.ShowToast</c> hands the strings to the tray balloon, whose
    /// Win32 struct marshals the title into 64 UTF-16 units and the text into 256, each including a
    /// terminator (verified by reflection on Hardcodet.NotifyIcon.Wpf 2.0.1's <c>NotifyIconData</c>,
    /// 2026-09-15). The shell cuts anything longer mid-line with no marker, so a group sized for a
    /// webhook named three or four accounts on the desktop and never said how many more existed.
    /// </summary>
    public static readonly PayloadLimits Toast = new(WebhookPayload.ToastTitleLimit, WebhookPayload.ToastBodyLimit);

    /// <summary>The envelope for <paramref name="destination"/>: the toast's for
    /// <see cref="AlertDestination.Local"/> (including a remote destination's desktop fallback, which
    /// the router has already turned into Local), the remote one for everything else.</summary>
    public static PayloadLimits For(AlertDestination destination) =>
        destination == AlertDestination.Local ? Toast : Remote;
}
