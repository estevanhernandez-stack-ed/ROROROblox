using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Real-shape Discord embed coverage + threshold-crossing logic + HTTP error-class behavior.
/// HttpClient is wired via a custom IHttpClientFactory so test assertions can read every
/// dispatched request.
/// </summary>
public class DiscordWebhookServiceTests
{
    private const string ValidWebhook = "https://discord.com/api/webhooks/123456789012345678/abcDEF_-secret-token";

    [Fact]
    public async Task PostLaunchAsync_OffByConfig_DoesNotPost()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(OnLaunch: false, false, false), ValidWebhook);
        await sut.PostLaunchAsync(1, CancellationToken.None);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task PostLaunchAsync_NullWebhook_DoesNotPost()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(true, false, false), webhookUrl: null);
        await sut.PostLaunchAsync(1, CancellationToken.None);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task PostLaunchAsync_InvalidWebhook_DoesNotPost()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(true, false, false), webhookUrl: "not-a-discord-url");
        await sut.PostLaunchAsync(1, CancellationToken.None);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task PostLaunchAsync_ValidWebhook_PostsBrandedEmbed()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(true, false, false), ValidWebhook);
        http.QueueResponse(HttpStatusCode.NoContent);

        await sut.PostLaunchAsync(2, CancellationToken.None);

        Assert.Single(http.Requests);
        var posted = http.Requests[0];
        Assert.Equal(ValidWebhook, posted.Url);

        using var doc = JsonDocument.Parse(posted.Body);
        Assert.Equal(DiscordWebhookService.Username, doc.RootElement.GetProperty("username").GetString());
        Assert.Equal(DiscordWebhookService.AvatarUrl, doc.RootElement.GetProperty("avatar_url").GetString());

        var embed = doc.RootElement.GetProperty("embeds")[0];
        Assert.Equal("Started ROROROblox", embed.GetProperty("title").GetString());
        Assert.Equal("2 accounts queued", embed.GetProperty("description").GetString());
        Assert.Equal(DiscordWebhookService.BrandColorCyan, embed.GetProperty("color").GetInt32());
        Assert.Equal(DiscordWebhookService.BrandFooter, embed.GetProperty("footer").GetProperty("text").GetString());
        Assert.True(embed.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task PostLaunchAsync_SingularGrammar_OnOneAccount()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(true, false, false), ValidWebhook);
        http.QueueResponse(HttpStatusCode.NoContent);

        await sut.PostLaunchAsync(1, CancellationToken.None);

        using var doc = JsonDocument.Parse(http.Requests[0].Body);
        Assert.Equal("1 account queued", doc.RootElement.GetProperty("embeds")[0].GetProperty("description").GetString());
    }

    [Fact]
    public async Task PostServerJoinAsync_IncludesUrlOnEmbed()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(false, OnPrivateServerJoin: true, false), ValidWebhook);
        http.QueueResponse(HttpStatusCode.NoContent);

        var shareUrl = "https://www.roblox.com/games/123/x?privateServerLinkCode=ABC";
        await sut.PostServerJoinAsync(shareUrl, CancellationToken.None);

        using var doc = JsonDocument.Parse(http.Requests[0].Body);
        Assert.Equal(shareUrl, doc.RootElement.GetProperty("embeds")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task PostAccountThresholdAsync_BelowThreshold_DoesNotPost()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(false, false, OnNAccountsActive: true), ValidWebhook);
        await sut.PostAccountThresholdAsync(3, CancellationToken.None);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task PostAccountThresholdAsync_CrossingFromBelow_FiresOnce()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(false, false, true), ValidWebhook);
        http.QueueResponse(HttpStatusCode.NoContent);

        await sut.PostAccountThresholdAsync(4, CancellationToken.None);

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task PostAccountThresholdAsync_StayingAbove_DoesNotRefire()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(false, false, true), ValidWebhook);
        http.QueueResponse(HttpStatusCode.NoContent);
        http.QueueResponse(HttpStatusCode.NoContent); // never consumed

        await sut.PostAccountThresholdAsync(4, CancellationToken.None);
        await sut.PostAccountThresholdAsync(5, CancellationToken.None);
        await sut.PostAccountThresholdAsync(6, CancellationToken.None);

        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task PostAccountThresholdAsync_DropsAndRecrosses_WithinQuietWindow_DoesNotRefire()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var (sut, http) = NewSut(new DiscordWebhookEvents(false, false, true), ValidWebhook, clock);
        http.QueueResponse(HttpStatusCode.NoContent);

        await sut.PostAccountThresholdAsync(4, CancellationToken.None); // fires
        await sut.PostAccountThresholdAsync(2, CancellationToken.None); // drops below
        clock.Advance(TimeSpan.FromMinutes(5));
        await sut.PostAccountThresholdAsync(4, CancellationToken.None); // re-cross within 30-min quiet window

        // Only the first crossing fired.
        Assert.Single(http.Requests);
    }

    [Fact]
    public async Task PostAccountThresholdAsync_DropsAndRecrosses_AfterQuietWindow_FiresAgain()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var (sut, http) = NewSut(new DiscordWebhookEvents(false, false, true), ValidWebhook, clock);
        http.QueueResponse(HttpStatusCode.NoContent);
        http.QueueResponse(HttpStatusCode.NoContent);

        await sut.PostAccountThresholdAsync(4, CancellationToken.None); // fires
        await sut.PostAccountThresholdAsync(1, CancellationToken.None); // drop
        clock.Advance(TimeSpan.FromMinutes(31));
        await sut.PostAccountThresholdAsync(4, CancellationToken.None); // recross AFTER quiet window

        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task Post_4xx_DoesNotRetry_DoesNotThrow()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(true, false, false), ValidWebhook);
        http.QueueResponse(HttpStatusCode.BadRequest);

        var ex = await Record.ExceptionAsync(() => sut.PostLaunchAsync(1, CancellationToken.None));

        Assert.Null(ex);
        Assert.Single(http.Requests); // no retry
    }

    [Fact]
    public async Task Post_5xx_RetriesOnce_ThenDrops()
    {
        var delays = new List<TimeSpan>();
        var (sut, http) = NewSut(new DiscordWebhookEvents(true, false, false), ValidWebhook,
            delayHook: (d, _) => { delays.Add(d); return Task.CompletedTask; });
        http.QueueResponse(HttpStatusCode.InternalServerError);
        http.QueueResponse(HttpStatusCode.InternalServerError);

        await sut.PostLaunchAsync(1, CancellationToken.None);

        Assert.Equal(2, http.Requests.Count);
        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(1), delays[0]);
    }

    [Fact]
    public async Task Post_5xx_ThenSuccess_StopsAfterSuccess()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(true, false, false), ValidWebhook,
            delayHook: (_, _) => Task.CompletedTask);
        http.QueueResponse(HttpStatusCode.BadGateway);
        http.QueueResponse(HttpStatusCode.NoContent);

        await sut.PostLaunchAsync(1, CancellationToken.None);

        Assert.Equal(2, http.Requests.Count);
    }

    [Fact]
    public async Task Post_429_WithRetryAfter_WaitsAndRetries()
    {
        var delays = new List<TimeSpan>();
        var (sut, http) = NewSut(new DiscordWebhookEvents(true, false, false), ValidWebhook,
            delayHook: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
        http.QueueResponse(rateLimited);
        http.QueueResponse(HttpStatusCode.NoContent);

        await sut.PostLaunchAsync(1, CancellationToken.None);

        Assert.Equal(2, http.Requests.Count);
        Assert.Single(delays);
        Assert.Equal(TimeSpan.FromSeconds(5), delays[0]);
    }

    [Fact]
    public async Task Post_429_WithRetryAfterAboveCap_Drops()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(true, false, false), ValidWebhook);
        var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60));
        http.QueueResponse(rateLimited);

        await sut.PostLaunchAsync(1, CancellationToken.None);

        Assert.Single(http.Requests); // no retry — Retry-After exceeded MaxRetryAfter cap
    }

    [Fact]
    public async Task Post_HttpRequestException_LogsAndDoesNotThrow()
    {
        var (sut, http) = NewSut(new DiscordWebhookEvents(true, false, false), ValidWebhook,
            delayHook: (_, _) => Task.CompletedTask);
        http.QueueException(new HttpRequestException("DNS fail"));
        http.QueueException(new HttpRequestException("DNS fail")); // retry path

        var ex = await Record.ExceptionAsync(() => sut.PostLaunchAsync(1, CancellationToken.None));
        Assert.Null(ex);
    }

    // ---- Helpers ----

    private static (DiscordWebhookService sut, FakeHttp http) NewSut(
        DiscordWebhookEvents events,
        string? webhookUrl,
        ManualTimeProvider? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delayHook = null)
    {
        var http = new FakeHttp();
        var factory = new TestHttpClientFactory(http);
        var config = new FakeConfig(events, webhookUrl);
        var sut = new DiscordWebhookService(
            config,
            factory,
            NullLogger<DiscordWebhookService>.Instance,
            clock ?? (TimeProvider)TimeProvider.System,
            defaultDelay: delayHook);
        return (sut, http);
    }

    private sealed class FakeConfig(DiscordWebhookEvents events, string? webhookUrl) : IDiscordConfig
    {
        public bool RichPresenceEnabled => true;
        public string? WebhookUrl { get; } = webhookUrl;
        public DiscordWebhookEvents WebhookEvents { get; } = events;
        public Task SaveAsync(DiscordConfigSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;
#pragma warning disable CS0067
        public event EventHandler? Changed;
#pragma warning restore CS0067
    }

    private sealed class TestHttpClientFactory(FakeHttp http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new FakeHandler(http)) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class FakeHttp
    {
        public List<RecordedRequest> Requests { get; } = [];
        public Queue<Func<HttpResponseMessage>> Responses { get; } = new();

        public void QueueResponse(HttpStatusCode status) =>
            Responses.Enqueue(() => new HttpResponseMessage(status));

        public void QueueResponse(HttpResponseMessage response) =>
            Responses.Enqueue(() => response);

        public void QueueException(Exception ex) =>
            Responses.Enqueue(() => throw ex);
    }

    private sealed record RecordedRequest(string Url, string Body);

    private sealed class FakeHandler(FakeHttp http) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            http.Requests.Add(new RecordedRequest(request.RequestUri!.ToString(), body));
            if (http.Responses.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return http.Responses.Dequeue()();
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }
}
