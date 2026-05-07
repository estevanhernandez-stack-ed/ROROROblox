using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// STUB — item 1 scaffolding. Real implementation lands in item 4: DiscordRpcClient (Lachee),
/// branded asset keys, party hash + Base64 join secret, JoinRequested event, 5/10/20/40/60s
/// reconnect backoff, IDiscordConfig.Changed subscription.
/// </summary>
public sealed class DiscordRichPresenceService : IDiscordPresence
{
    private readonly IDiscordConfig _config;
    private readonly ILogger<DiscordRichPresenceService> _log;

    public DiscordRichPresenceService(IDiscordConfig config, ILogger<DiscordRichPresenceService> log)
    {
        _config = config;
        _log = log;
        _log.LogDebug("DiscordRichPresenceService stub constructed (item 1 scaffolding); real impl in item 4.");
    }

    public Task StartAsync(CancellationToken ct)
    {
        _log.LogDebug("DiscordRichPresenceService.StartAsync stub — no IPC connect until item 4. (RichPresenceEnabled={Enabled})", _config.RichPresenceEnabled);
        return Task.CompletedTask;
    }

    public Task UpdateStateAsync(RichPresenceState state, CancellationToken ct) => Task.CompletedTask;
    public Task SetPartyAsync(string serverShareUrl, CancellationToken ct) => Task.CompletedTask;
    public Task ClearPartyAsync(CancellationToken ct) => Task.CompletedTask;

    public event EventHandler<JoinRequestedEventArgs>? JoinRequested;

    // Suppress CS0067 — real impl in item 4 raises this from DiscordRpcClient.OnJoin.
    private void RaiseJoinRequested(string url) => JoinRequested?.Invoke(this, new JoinRequestedEventArgs(url));

    public ValueTask DisposeAsync()
    {
        _log.LogDebug("DiscordRichPresenceService.DisposeAsync stub.");
        return ValueTask.CompletedTask;
    }
}
