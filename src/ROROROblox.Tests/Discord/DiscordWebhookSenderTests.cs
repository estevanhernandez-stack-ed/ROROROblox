using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class DiscordWebhookSenderTests
{
    private static readonly WebhookPayload Payload = new("BaronBloxwell dropped out", "• BaronBloxwell — Pet Simulator 99!");
    private const string Url = "https://discord.com/api/webhooks/1/tok";

    [Fact]
    public async Task SendAsync_Success_ReportsSentAndPostsTheText()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sender = new DiscordWebhookSender(new HttpClient(handler), NullLogger.Instance);

        var result = await sender.SendAsync(Url, Payload).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WebhookSendResult.Sent, result);
        Assert.Contains("BaronBloxwell", Assert.Single(handler.Bodies), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendAsync_404_ReportsWebhookGoneAndDoesNotRetry()
    {
        // A deleted webhook never comes back. Retrying it forever is how a background loop
        // outlives the reason for it.
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sender = new DiscordWebhookSender(new HttpClient(handler), NullLogger.Instance);

        var result = await sender.SendAsync(Url, Payload).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WebhookSendResult.WebhookGone, result);
        Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task SendAsync_429_ReportsRateLimited()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var sender = new DiscordWebhookSender(new HttpClient(handler), NullLogger.Instance);

        var result = await sender.SendAsync(Url, Payload).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WebhookSendResult.RateLimited, result);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_ReportsFailedRatherThanThrowing()
    {
        // No Discord failure may affect the app. An alert is a passenger too.
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("no network"));
        var sender = new DiscordWebhookSender(new HttpClient(handler), NullLogger.Instance);

        var result = await sender.SendAsync(Url, Payload).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WebhookSendResult.Failed, result);
    }

    [Fact]
    public async Task SendAsync_AFailure_NeverLogsTheWebhookUrl()
    {
        // The URL is a bearer credential — anyone holding it can post to that channel forever.
        // Log files get pasted into Discord by users asking why alerts stopped arriving, so the
        // one place a failing webhook is most likely to be written down must not carry the token.
        var log = new CapturingLogger<DiscordWebhookSender>();
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("no network"));
        var sender = new DiscordWebhookSender(new HttpClient(handler), log);

        await sender.SendAsync(Url, Payload).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(log.Snapshot(), line =>
        {
            Assert.DoesNotContain("tok", line, StringComparison.Ordinal);
            Assert.DoesNotContain(Url, line, StringComparison.Ordinal);
        });
    }
}
