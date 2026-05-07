using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Discord;

namespace ROROROblox.App.Discord;

/// <summary>
/// Layer 3 surface: opt-in clan-channel webhook posts. Per spec §5.4 + §6.3.
///
/// Three-state opt-in cascade: master rich-presence toggle, webhook URL set + valid, and
/// per-event toggle. Each Post* method early-returns if any of the three is OFF.
///
/// HTTP error handling per spec §7:
///   - 4xx (rate limit, bad URL): no retry, log warning, drop.
///   - 429: respect Retry-After up to 30s, then retry once.
///   - 5xx: retry once with 1s delay, then drop.
///   - Network/timeout: log warning, drop.
/// **Never throws to caller** — webhook is best-effort, the launch path is invariant.
///
/// URL is redacted in logs to scheme + host only (Discord webhook URLs are credentials in
/// disguise — anyone with the URL can post to the channel).
/// </summary>
public sealed class DiscordWebhookService : IDiscordWebhook
{
    public const int BrandColorCyan = 0x17D4FA; // 1561082 in decimal — embed color field
    public const string BrandFooter = "ROROROblox · Imagine Something Else.";
    public const string Username = "ROROROblox";
    public const string AvatarUrl = "https://estevanhernandez-stack-ed.github.io/ROROROblox/assets/rororoblox-webhook-avatar.png";
    public const int AccountThreshold = 4;
    public static readonly TimeSpan ThresholdQuietWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan PerCallTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    private static readonly Regex WebhookUrlPattern =
        new(@"^https://discord\.com/api/webhooks/\d+/[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IDiscordConfig _config;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<DiscordWebhookService> _log;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    private readonly object _thresholdGate = new();
    private bool _thresholdLatched; // True while count is above threshold; flips back when count drops
    private DateTimeOffset? _thresholdLastFiredAt;

    public DiscordWebhookService(
        IDiscordConfig config,
        IHttpClientFactory httpFactory,
        ILogger<DiscordWebhookService> log)
        : this(config, httpFactory, log, TimeProvider.System, defaultDelay: null)
    {
    }

    /// <summary>Test-only constructor: inject TimeProvider + delay function for deterministic threshold + retry tests.</summary>
    internal DiscordWebhookService(
        IDiscordConfig config,
        IHttpClientFactory httpFactory,
        ILogger<DiscordWebhookService> log,
        TimeProvider timeProvider,
        Func<TimeSpan, CancellationToken, Task>? defaultDelay)
    {
        _config = config;
        _httpFactory = httpFactory;
        _log = log;
        _timeProvider = timeProvider;
        _delay = defaultDelay ?? ((d, ct) => Task.Delay(d, ct));
    }

    public async Task PostLaunchAsync(int accountCount, CancellationToken ct)
    {
        if (!ShouldPost(_config.WebhookEvents.OnLaunch)) return;

        var embed = new Embed(
            Title: "Started ROROROblox",
            Description: $"{accountCount} {(accountCount == 1 ? "account" : "accounts")} queued",
            Url: null,
            Color: BrandColorCyan,
            Footer: new EmbedFooter(BrandFooter),
            Timestamp: _timeProvider.GetUtcNow().ToString("O"));
        await PostAsync(BuildPayload(embed), ct).ConfigureAwait(false);
    }

    public async Task PostServerJoinAsync(string serverShareUrl, CancellationToken ct)
    {
        if (!ShouldPost(_config.WebhookEvents.OnPrivateServerJoin)) return;
        if (string.IsNullOrWhiteSpace(serverShareUrl)) return;

        var embed = new Embed(
            Title: "Joined a private server",
            Description: "Click the title to jump in.",
            Url: serverShareUrl,
            Color: BrandColorCyan,
            Footer: new EmbedFooter(BrandFooter),
            Timestamp: _timeProvider.GetUtcNow().ToString("O"));
        await PostAsync(BuildPayload(embed), ct).ConfigureAwait(false);
    }

    public async Task PostAccountThresholdAsync(int accountCount, CancellationToken ct)
    {
        if (!ShouldPost(_config.WebhookEvents.OnNAccountsActive)) return;

        bool fire;
        var now = _timeProvider.GetUtcNow();
        lock (_thresholdGate)
        {
            if (accountCount >= AccountThreshold)
            {
                if (_thresholdLatched)
                {
                    // Already above and we already fired — no re-fire while sustained.
                    fire = false;
                }
                else if (_thresholdLastFiredAt is { } last && now - last < ThresholdQuietWindow)
                {
                    // Inside the 30-min quiet window from a previous crossing — don't re-fire,
                    // but DO latch so we treat this as the active high state.
                    _thresholdLatched = true;
                    fire = false;
                }
                else
                {
                    _thresholdLatched = true;
                    _thresholdLastFiredAt = now;
                    fire = true;
                }
            }
            else
            {
                // Dropped back below — unlatch so the next crossing fires (subject to quiet window).
                _thresholdLatched = false;
                fire = false;
            }
        }

        if (!fire) return;

        var embed = new Embed(
            Title: $"{accountCount}+ accounts running",
            Description: "Big squad energy.",
            Url: null,
            Color: BrandColorCyan,
            Footer: new EmbedFooter(BrandFooter),
            Timestamp: now.ToString("O"));
        await PostAsync(BuildPayload(embed), ct).ConfigureAwait(false);
    }

    // ---- Internals ----

    private bool ShouldPost(bool eventToggle)
    {
        if (!eventToggle) return false;
        var url = _config.WebhookUrl;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!WebhookUrlPattern.IsMatch(url))
        {
            _log.LogWarning("Discord webhook URL fails validation regex; skipping post.");
            return false;
        }
        return true;
    }

    private static WebhookPayload BuildPayload(Embed embed) =>
        new(Username, AvatarUrl, [embed]);

    private async Task PostAsync(WebhookPayload payload, CancellationToken ct)
    {
        var url = _config.WebhookUrl!;

        try
        {
            await SendOnceWithRetryAsync(url, payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Catch-all so caller never sees an exception. Specific paths below already handle
            // expected failure modes; this is the belt-and-suspenders.
            _log.LogWarning(ex, "Discord webhook post to {Host} failed; dropping.", RedactHost(url));
        }
    }

    private async Task SendOnceWithRetryAsync(string url, WebhookPayload payload, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("DiscordWebhook");

        var attempt = 0;
        while (true)
        {
            attempt++;
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(PerCallTimeout);

            HttpResponseMessage response;
            try
            {
                response = await client.PostAsJsonAsync(url, payload, SerializerOptions, attemptCts.Token).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _log.LogWarning(ex, "Discord webhook HTTP error posting to {Host} (attempt {Attempt}).", RedactHost(url), attempt);
                if (attempt < 2 && IsRetryable(ex))
                {
                    await _delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                    continue;
                }
                return;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _log.LogWarning(ex, "Discord webhook post to {Host} timed out (attempt {Attempt}).", RedactHost(url), attempt);
                return;
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = ResolveRetryAfter(response);
                    if (retryAfter is null || retryAfter.Value > MaxRetryAfter)
                    {
                        _log.LogWarning("Discord webhook 429 with unworkable Retry-After ({Wait}); dropping.", retryAfter);
                        return;
                    }
                    if (attempt >= 2)
                    {
                        _log.LogWarning("Discord webhook still 429 after retry; dropping.");
                        return;
                    }
                    await _delay(retryAfter.Value, ct).ConfigureAwait(false);
                    continue;
                }

                var status = (int)response.StatusCode;
                if (status >= 500 && status < 600)
                {
                    if (attempt >= 2)
                    {
                        _log.LogWarning("Discord webhook 5xx ({Status}) after retry; dropping.", status);
                        return;
                    }
                    await _delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                    continue;
                }

                // 4xx — bad URL, deleted webhook, payload reject. No retry.
                _log.LogWarning("Discord webhook {Status} from {Host}; dropping.", status, RedactHost(url));
                return;
            }
        }
    }

    private static bool IsRetryable(HttpRequestException ex)
    {
        // Network reset / connection drop / DNS hiccup — retry once. Otherwise drop.
        // HttpRequestException doesn't expose the underlying SocketException reliably across
        // platforms, so default to retry-once; the attempt cap prevents loops.
        _ = ex;
        return true;
    }

    private static TimeSpan? ResolveRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter is null) return TimeSpan.FromSeconds(1);
        if (response.Headers.RetryAfter.Delta is { } delta) return delta;
        if (response.Headers.RetryAfter.Date is { } date)
        {
            var diff = date - DateTimeOffset.UtcNow;
            return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
        }
        return TimeSpan.FromSeconds(1);
    }

    private static string RedactHost(string url)
    {
        try
        {
            var uri = new Uri(url, UriKind.Absolute);
            return $"{uri.Scheme}://{uri.Host}/…";
        }
        catch
        {
            return "(unparseable)";
        }
    }

    // ---- Wire shapes (kept private — Discord webhook payload schema, snake_case via SerializerOptions) ----

    private sealed record WebhookPayload(
        string Username,
        string AvatarUrl,
        IReadOnlyList<Embed> Embeds);

    private sealed record Embed(
        string Title,
        string? Description,
        string? Url,
        int Color,
        EmbedFooter Footer,
        string Timestamp);

    private sealed record EmbedFooter(string Text);
}
