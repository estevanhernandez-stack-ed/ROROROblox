using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// STUB — item 1 scaffolding. Real implementation lands in item 7: subscribes to
/// IDiscordPresence.JoinRequested + IAccountLifecycle.AccountStarted/Stopped, posts webhooks,
/// resolves most-recent account for inbound joins. The keystone wiring service.
///
/// Implements IHostedService for shape parity with future Generic Host migration; the App
/// currently uses raw ServiceCollection and bootstraps StartAsync/StopAsync manually from
/// App.OnStartup / OnExit.
/// </summary>
public sealed class DiscordPresenceLifecycle : IHostedService
{
    private readonly IDiscordPresence _presence;
    private readonly ILogger<DiscordPresenceLifecycle> _log;

    public DiscordPresenceLifecycle(
        IDiscordPresence presence,
        ILogger<DiscordPresenceLifecycle> log)
    {
        _presence = presence;
        _log = log;
        _log.LogDebug("DiscordPresenceLifecycle stub constructed (item 1 scaffolding); real impl in item 7.");
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _log.LogInformation("DiscordPresenceLifecycle.StartAsync — starting Discord presence (stub wiring; real handlers land in item 7).");
        await _presence.StartAsync(ct).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _log.LogInformation("DiscordPresenceLifecycle.StopAsync — disposing Discord presence.");
        try
        {
            await _presence.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "DiscordPresenceLifecycle dispose threw; ignoring.");
        }
    }
}
