using System.Net;
using ROROROblox.App.Discord;

namespace ROROROblox.Tests.Discord;

public class WebhookProbeTests
{
    private const string Url = "https://discord.com/api/webhooks/1/tok";

    [Fact]
    public async Task DescribeAsync_ValidWebhook_ReturnsTheChannelAndServerNames()
    {
        // So a clan webhook pasted into the personal slot is visible BEFORE it matters,
        // not after the first alert lands in the wrong channel.
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"name":"rororo","channel_id":"1","guild_id":"2","source_channel":{"name":"rororo-alerts"},"source_guild":{"name":"Este's Server"}}"""),
        });

        var identity = await new WebhookProbe(new HttpClient(handler))
            .DescribeAsync(Url).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("rororo-alerts", identity!.ChannelName);
        Assert.Equal("Este's Server", identity.GuildName);
    }

    [Fact]
    public async Task DescribeAsync_DeletedWebhook_ReturnsNullRatherThanThrowing()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await new WebhookProbe(new HttpClient(handler))
            .DescribeAsync(Url).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task DescribeAsync_NetworkFailure_ReturnsNull()
    {
        var handler = new StubHttpHandler(_ => throw new HttpRequestException("offline"));

        Assert.Null(await new WebhookProbe(new HttpClient(handler))
            .DescribeAsync(Url).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task DescribeAsync_MalformedJson_ReturnsNull()
    {
        // Anything that isn't the shape we expect is "we don't know," not a crash in Settings.
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json at all"),
        });

        Assert.Null(await new WebhookProbe(new HttpClient(handler))
            .DescribeAsync(Url).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task DescribeAsync_AWebhookWithNoSourceMetadata_ReturnsNull()
    {
        // A valid 200 that simply doesn't carry source_channel/source_guild. Reporting
        // "#unknown in your server" would be noise dressed up as information.
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"name":"rororo","channel_id":"1","guild_id":"2"}"""),
        });

        Assert.Null(await new WebhookProbe(new HttpClient(handler))
            .DescribeAsync(Url).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DescribeAsync_NoUrl_ReturnsNullWithoutCallingOut(string? url)
    {
        var handler = new StubHttpHandler(_ => throw new InvalidOperationException("should not be called"));

        Assert.Null(await new WebhookProbe(new HttpClient(handler))
            .DescribeAsync(url!).WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
