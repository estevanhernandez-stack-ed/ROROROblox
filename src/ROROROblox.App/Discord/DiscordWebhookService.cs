using System.Net.Http;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// STUB — item 1 scaffolding. Real implementation lands in item 8: branded embed (color
/// 0x17D4FA, "ROROROblox · Imagine Something Else." footer), threshold-crossing logic,
/// 5xx retry once, 429 Retry-After respect, URL redacted in logs, never throws.
/// </summary>
public sealed class DiscordWebhookService : IDiscordWebhook
{
    private readonly IDiscordConfig _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<DiscordWebhookService> _log;

    public DiscordWebhookService(
        IDiscordConfig config,
        IHttpClientFactory httpFactory,
        ILogger<DiscordWebhookService> log)
    {
        _config = config;
        _httpFactory = httpFactory;
        _log = log;
        _log.LogDebug("DiscordWebhookService stub constructed (item 1 scaffolding); real impl in item 8.");
    }

    public Task PostLaunchAsync(int accountCount, CancellationToken ct) => Task.CompletedTask;
    public Task PostServerJoinAsync(string serverShareUrl, CancellationToken ct) => Task.CompletedTask;
    public Task PostAccountThresholdAsync(int accountCount, CancellationToken ct) => Task.CompletedTask;
}
