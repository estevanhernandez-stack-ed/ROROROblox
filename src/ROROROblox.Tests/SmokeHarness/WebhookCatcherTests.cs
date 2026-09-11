using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.Core.Discord;
using ROROROblox.MetricSmoke;

namespace ROROROblox.Tests.SmokeHarness;

/// <summary>
/// <see cref="WebhookCatcher"/> is the only way any scenario in this harness can see what the app
/// actually POSTed to a webhook — the app's own log carries an alert's title but never its body
/// (spec §0.4). Every test here drives it over real loopback sockets rather than in-process calls,
/// because the whole point of the class is to behave like Discord's real endpoint to code
/// (<c>DiscordWebhookSender</c>) that only ever talks HTTP.
/// <para>
/// Each test starts and disposes its own catcher on its own ephemeral port (bound via
/// <c>TcpListener(0)</c> inside <see cref="WebhookCatcher.Start"/>), so nothing here needs to
/// serialize against xUnit running other test classes in parallel — there is no shared or fixed
/// port for two tests to collide on, and every catcher is disposed before its test returns.
/// </para>
/// </summary>
public sealed class WebhookCatcherTests
{
    private static readonly HttpClient Http = new();

    [Fact]
    public async Task DrainAsync_APostToTheMinePath_IsCapturedWithItsBodyIntact()
    {
        await using var catcher = WebhookCatcher.Start();

        await PostAsync(catcher.MineUrl, "the exact body the app sent");

        var posts = await catcher.DrainAsync(TimeSpan.FromSeconds(5));

        var post = Assert.Single(posts);
        Assert.Equal(WebhookCatcher.MinePath, post.Path);
        Assert.Equal("the exact body the app sent", post.Body);
    }

    [Fact]
    public async Task DrainAsync_MineAndClanPosts_AreDistinguishableByPath()
    {
        await using var catcher = WebhookCatcher.Start();

        await PostAsync(catcher.MineUrl, "mine body");
        await PostAsync(catcher.ClanUrl, "clan body");

        var posts = await catcher.DrainAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, posts.Count);
        Assert.Contains(posts, p => p.Path == WebhookCatcher.MinePath && p.Body == "mine body");
        Assert.Contains(posts, p => p.Path == WebhookCatcher.ClanPath && p.Body == "clan body");
    }

    [Fact]
    public async Task DrainAsync_NothingArrives_ReturnsEmpty_RatherThanHanging()
    {
        await using var catcher = WebhookCatcher.Start();

        // Bounded from the outside too: if DrainAsync ever regressed into an unconditional wait,
        // this test should time out and fail loudly rather than hang the whole suite.
        var posts = await catcher.DrainAsync(TimeSpan.FromMilliseconds(300)).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(posts);
    }

    [Fact]
    public async Task DrainAsync_TwoPosts_AreReturnedInArrivalOrder()
    {
        await using var catcher = WebhookCatcher.Start();

        await PostAsync(catcher.MineUrl, "first");
        await PostAsync(catcher.ClanUrl, "second");

        var posts = await catcher.DrainAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, posts.Count);
        Assert.Equal("first", posts[0].Body);
        Assert.Equal("second", posts[1].Body);
    }

    [Fact]
    public async Task DisposeAsync_ReleasesThePort_SoASecondCatcherCanStartOnTheSameOne()
    {
        var first = WebhookCatcher.Start();
        var port = new Uri(first.MineUrl).Port;

        await first.DisposeAsync();

        // Binding directly on the exact port the first catcher held is the real proof of
        // release -- a fresh WebhookCatcher.Start() would pick its own new port via
        // TcpListener(0) and would pass even if this exact one had leaked.
        var rebound = new HttpListener();
        rebound.Prefixes.Add($"http://127.0.0.1:{port}/");
        rebound.Start();
        try
        {
            Assert.True(rebound.IsListening);
        }
        finally
        {
            rebound.Close();
        }
    }

    /// <summary>
    /// The case a reviewer would ask for first: does this thing actually stand in for Discord to
    /// the app's REAL sender, unmocked, over real sockets? A stub <c>HttpMessageHandler</c>
    /// returning 204 (as <c>DiscordWebhookSenderTests</c> already does) proves the sender's own
    /// branching logic; it does not prove THIS listener produces a response the sender's HttpClient
    /// stack actually accepts as that branch off the wire -- a missing Content-Length, a connection
    /// closed oddly, or anything else between "the app posts" and "IsSuccessStatusCode is
    /// evaluated" could still trip something a mocked handler can't expose. This test removes
    /// every mock from that path.
    /// </summary>
    [Fact]
    public async Task RealDiscordWebhookSender_PostingToTheCatcher_ReportsSentAndTheBodyIsCaptured()
    {
        await using var catcher = WebhookCatcher.Start();
        var sender = new DiscordWebhookSender(new HttpClient(), NullLogger<DiscordWebhookSender>.Instance);
        var payload = new WebhookPayload("BaronBloxwell dropped out", "• BaronBloxwell — Pet Simulator 99!");

        var result = await sender.SendAsync(catcher.MineUrl, payload).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WebhookSendResult.Sent, result);
        var post = Assert.Single(await catcher.DrainAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(WebhookCatcher.MinePath, post.Path);
        Assert.Contains("BaronBloxwell", post.Body, StringComparison.Ordinal);
    }

    private static async Task PostAsync(string url, string body)
    {
        using var response = await Http.PostAsync(url, new StringContent(body, Encoding.UTF8));
        response.EnsureSuccessStatusCode();
    }
}
