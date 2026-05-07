namespace ROROROblox.Core.Discord;

/// <summary>
/// Read-mostly, written-rarely (only when the Settings UI saves). Singleton-cached and
/// file-watcher reloaded so consumers stay reactive without an app restart.
/// </summary>
public interface IDiscordConfig
{
    bool RichPresenceEnabled { get; }
    string? WebhookUrl { get; }
    DiscordWebhookEvents WebhookEvents { get; }

    Task SaveAsync(DiscordConfigSnapshot snapshot, CancellationToken ct);
    event EventHandler? Changed;
}

public sealed record DiscordConfigSnapshot(
    bool RichPresenceEnabled,
    string? WebhookUrl,
    DiscordWebhookEvents WebhookEvents);

public sealed record DiscordWebhookEvents(
    bool OnLaunch,
    bool OnPrivateServerJoin,
    bool OnNAccountsActive)
{
    public static DiscordWebhookEvents AllOff { get; } = new(false, false, false);
}
