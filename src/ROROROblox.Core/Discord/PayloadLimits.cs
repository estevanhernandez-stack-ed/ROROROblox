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

    /// <summary>
    /// <paramref name="text"/> cut to <see cref="Title"/> units, with a trailing <c>…</c> when it
    /// had to be. See <see cref="Clamp"/> for why this exists at all.
    /// </summary>
    public string ClampTitle(string text) => Clamp(text, Title);

    /// <summary><paramref name="text"/> cut to <see cref="Body"/> units. See <see cref="Clamp"/>.</summary>
    public string ClampBody(string text) => Clamp(text, Body);

    /// <summary>
    /// The envelope as a BACKSTOP rather than only a budget (v1.33 item 4).
    /// <para>
    /// Through v1.32 these numbers were advice the dispatcher followed and the SHELL enforced: a
    /// caller handing <c>TrayService.ShowToast</c> its own longer strings got Windows' silent
    /// mid-line cut, which is what <see cref="Toast"/>'s own note describes. Item 4 draws the
    /// balloon, so nothing cuts any more — an over-long string would just render, and a
    /// notification would be sized by its content instead of by its envelope.
    /// </para>
    /// <para>
    /// It should never fire in production: <see cref="WebhookPayload.ForAlert"/> already builds
    /// every routed payload inside the destination's envelope. That is exactly why the <c>…</c> is
    /// here — if it ever shows up on screen, something upstream stopped honouring the limits, and a
    /// visible marker is the difference between noticing that and not.
    /// </para>
    /// <para>
    /// Delegates the cut to <see cref="WebhookPayload.Front"/> rather than slicing: an emoji in an
    /// account name is a surrogate PAIR, and cutting between its halves leaves a lone surrogate
    /// that renders as a replacement box. One implementation of that care, not two.
    /// </para>
    /// </summary>
    private static string Clamp(string text, int limit)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length <= limit ? text : WebhookPayload.Front(text, limit - 1) + '…';
    }

    /// <summary>The envelope for <paramref name="destination"/>: the toast's for
    /// <see cref="AlertDestination.Local"/> (including a remote destination's desktop fallback, which
    /// the router has already turned into Local), the remote one for everything else.</summary>
    public static PayloadLimits For(AlertDestination destination) =>
        destination == AlertDestination.Local ? Toast : Remote;
}
