using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// STUB — item 1 scaffolding. Real implementation lands in item 2: atomic JSON write,
/// file-watcher Changed event, malformed-recovery-with-corrupt-file-preservation.
/// </summary>
public sealed class DiscordConfigStore : IDiscordConfig
{
    private readonly ILogger<DiscordConfigStore> _log;

    public DiscordConfigStore(ILogger<DiscordConfigStore> log)
    {
        _log = log;
        _log.LogDebug("DiscordConfigStore stub constructed (item 1 scaffolding); real impl in item 2.");
    }

    public bool RichPresenceEnabled => false;
    public string? WebhookUrl => null;
    public DiscordWebhookEvents WebhookEvents => DiscordWebhookEvents.AllOff;

    public Task SaveAsync(DiscordConfigSnapshot snapshot, CancellationToken ct)
    {
        _log.LogDebug("DiscordConfigStore.SaveAsync stub — no-op until item 2.");
        return Task.CompletedTask;
    }

    public event EventHandler? Changed;

    // Suppress CS0067 (event never invoked) — real impl in item 2 fires this from the file watcher.
    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
