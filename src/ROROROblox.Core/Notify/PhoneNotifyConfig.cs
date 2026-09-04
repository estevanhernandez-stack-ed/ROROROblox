namespace ROROROblox.Core.Notify;

/// <summary>Which push service carries phone alerts. <see cref="None"/> means the feature is off.</summary>
public enum PhoneProvider
{
    None,
    Pushover,
    Ntfy,
}

/// <summary>
/// Phone-alert settings. Everything defaults off — nothing leaves the machine until the user
/// picks a provider and pastes its credentials. Deliberately NOT part of <c>DiscordConfig</c>:
/// the 2026-09-04 ruling keeps the Discord webhook alerts their own separate piece, and a file
/// named <c>discord.dat</c> should not hold Pushover keys. Same DPAPI envelope, its own file
/// (<c>notify.dat</c>, see <see cref="PhoneNotifyConfigStore"/>).
/// <para>
/// Every credential here is a bearer credential. A Pushover user key + application token pair
/// lets anyone push to that phone; an ntfy topic grants both subscribe AND publish — whoever
/// holds it can read alerts and spoof notifications under RoRoRo's name. They get the same
/// treatment webhook URLs get: DPAPI at rest, masked in Settings, never logged.
/// </para>
/// </summary>
public sealed record PhoneNotifyConfig
{
    public PhoneProvider Provider { get; init; }

    /// <summary>Pushover "user key" from the user's own account (the 30-char key on their dashboard).</summary>
    public string? PushoverUserKey { get; init; }

    /// <summary>
    /// Pushover application token from the user's OWN application registration. Per the
    /// 2026-09-04 spec: no shared 626 Labs token ships in the binary — a shipped bearer token is
    /// extractable, pools the whole clan onto one message quota, and contradicts Pushover's
    /// redistribution guidance.
    /// </summary>
    public string? PushoverAppToken { get; init; }

    /// <summary>
    /// The ntfy topic — generated cryptographically random by <see cref="NtfyTopicGenerator"/>,
    /// never typed by hand: the topic IS the entire credential.
    /// </summary>
    public string? NtfyTopic { get; init; }

    /// <summary>
    /// Publish server. The default is the public ntfy.sh instance; user-overridable, which is
    /// what absorbs the self-host use case (the spec's answer to Gotify).
    /// </summary>
    public string NtfyServerUrl { get; init; } = "https://ntfy.sh";

    /// <summary>
    /// Whether the selected provider has everything it needs to actually send. This is the
    /// question <c>AlertRouter</c> asks before routing to Phone — configured-but-incomplete
    /// falls back to the desktop toast, same as a webhook destination with no URL.
    /// </summary>
    public bool IsConfigured => Provider switch
    {
        PhoneProvider.Pushover =>
            !string.IsNullOrWhiteSpace(PushoverUserKey) && !string.IsNullOrWhiteSpace(PushoverAppToken),
        PhoneProvider.Ntfy =>
            !string.IsNullOrWhiteSpace(NtfyTopic) && !string.IsNullOrWhiteSpace(NtfyServerUrl),
        _ => false,
    };
}
