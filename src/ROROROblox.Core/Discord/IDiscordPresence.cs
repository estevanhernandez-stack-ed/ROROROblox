namespace ROROROblox.Core.Discord;

/// <summary>
/// Layer 1 surface: per-user Discord rich presence + party "Join" button.
/// Wraps the local Discord IPC pipe; failures are silent (Discord is comfort, not load-bearing).
/// </summary>
public interface IDiscordPresence : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct);
    Task UpdateStateAsync(RichPresenceState state, CancellationToken ct);
    Task SetPartyAsync(string serverShareUrl, CancellationToken ct);
    Task ClearPartyAsync(CancellationToken ct);

    event EventHandler<JoinRequestedEventArgs>? JoinRequested;
}
