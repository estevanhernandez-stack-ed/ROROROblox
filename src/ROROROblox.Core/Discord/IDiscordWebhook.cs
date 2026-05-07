namespace ROROROblox.Core.Discord;

/// <summary>
/// Layer 3 surface: opt-in clan-channel webhook posts. All Post* methods early-return when
/// the corresponding event toggle is OFF or the URL is unset/invalid; never throw to caller.
/// </summary>
public interface IDiscordWebhook
{
    Task PostLaunchAsync(int accountCount, CancellationToken ct);
    Task PostServerJoinAsync(string serverShareUrl, CancellationToken ct);
    Task PostAccountThresholdAsync(int accountCount, CancellationToken ct);
}
