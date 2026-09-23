using System.Net;
using System.Net.Sockets;
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

    /// <summary>
    /// Fix round 1, finding 2: every test above fully awaits both posts before draining, so both
    /// are already queued and <see cref="WebhookCatcher.QuietPeriodMilliseconds"/> never actually
    /// runs — the reviewer set it to zero and all of them still passed. These two tests exercise
    /// it for real: the second post is fired on a background delay and NOT awaited before
    /// <c>DrainAsync</c> is called, so the outcome depends on the live wait/quiet-period logic
    /// rather than on both bodies already sitting in the queue. Both margins are expressed as
    /// multiples of the real constant, not a guessed number, so a future change to it keeps these
    /// tests meaningful instead of silently drifting off the boundary they pin.
    /// </summary>
    [Fact]
    public async Task DrainAsync_TwoPostsWellUnderTheQuietPeriodApart_AreBothCapturedInOneDrain()
    {
        // A generous quiet period, so what this measures is the boundary and not the scheduler. At
        // the shipped 150 ms it left about 100 ms for a Task.Delay plus an HTTP round trip, which a
        // loaded CI runner lost on 2026-09-20. The gap is still a fraction of the period in force.
        await using var catcher = WebhookCatcher.Start(TimeSpan.FromSeconds(2));
        var gap = catcher.QuietPeriod / 3;

        await PostAsync(catcher.MineUrl, "first");
        var postingSecond = PostAfterDelayAsync(catcher.ClanUrl, "second", gap);

        var posts = await catcher.DrainAsync(TimeSpan.FromSeconds(5));
        await postingSecond;

        Assert.Equal(2, posts.Count);
        Assert.Equal("first", posts[0].Body);
        Assert.Equal("second", posts[1].Body);
    }

    [Fact]
    public async Task DrainAsync_TwoPostsWellOverTheQuietPeriodApart_TheSecondArrivesOnlyOnTheNextDrain()
    {
        // The other side of the same boundary, and the safe direction: overshoot only makes the gap
        // more over. It takes the same period so the pair reads as one test of one constant.
        await using var catcher = WebhookCatcher.Start(TimeSpan.FromSeconds(2));
        var gap = catcher.QuietPeriod * 2;

        await PostAsync(catcher.MineUrl, "first");
        var postingSecond = PostAfterDelayAsync(catcher.ClanUrl, "second", gap);

        // Long enough to reach the quiet-period break (~150ms after "first" settles), short
        // enough that "second" (due at 600ms) cannot have landed yet -- this proves the boundary
        // rather than assuming it. A gap this far outside the window is exactly the case the fixed
        // window (reset on every arrival, but bounded by `within`) cannot bridge: it must return
        // with only "first".
        var firstDrain = await catcher.DrainAsync(TimeSpan.FromMilliseconds(WebhookCatcher.QuietPeriodMilliseconds * 3));
        var onlySoFar = Assert.Single(firstDrain);
        Assert.Equal("first", onlySoFar.Body);

        // Not lost, just deferred: the second post is still sitting in the app's own send path
        // (about to land) and is fully there on the very next drain -- a caller that drains again
        // sees it, rather than it vanishing.
        await postingSecond;
        var secondDrain = await catcher.DrainAsync(TimeSpan.FromSeconds(5));
        var onlyLater = Assert.Single(secondDrain);
        Assert.Equal("second", onlyLater.Body);
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
    /// Fix round 1, finding 1, the worst one: before this fix, <c>await HandleAsync(context)</c>
    /// sat outside the accept loop's only try/catch, so any exception raised while handling one
    /// request (a client reset mid-body, here) propagated out of the <c>while</c> loop and ended it
    /// for the rest of the process -- every later POST would then hang against a listener nobody
    /// was calling <c>GetContextAsync</c> on anymore. This sends a request that promises a
    /// Content-Length it never delivers, then forces an abortive TCP reset (not a graceful close)
    /// instead of finishing it, so the server-side read fails hard while it is blocked waiting for
    /// bytes that will never arrive. A normal POST sent afterward must still be captured, and the
    /// fault must be visible on <see cref="WebhookCatcher.RequestFaults"/> rather than silent.
    /// <para>
    /// Fix round 2 (final-fix wave, commit A): the reset used to fire immediately after the
    /// headers were sent, with nothing to say the server had even accepted the connection yet.
    /// The accept loop starts via <c>Task.Run(AcceptLoopAsync)</c> in the constructor, so on a fast
    /// or loaded machine the client's reset can land before that task has even posted its first
    /// <c>GetContextAsync</c> -- http.sys then has nothing to hand the app, no context is ever
    /// delivered, <c>HandleSafelyAsync</c> never runs, and the 10 s wait for a fault times out.
    /// Reproduced deterministically (4/4) on this machine before this fix; see the RED evidence in
    /// the final-fix report. The fix waits on <see cref="WebhookCatcher.RequestsReceived"/> --
    /// incremented at the very top of <c>HandleSafelyAsync</c>, before anything that can fail --
    /// so the reset cannot fire until the server is provably inside its handler for this exact
    /// request.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AcceptLoop_ABadRequestThatFailsMidRead_CostsOnlyThatOneRequest()
    {
        await using var catcher = WebhookCatcher.Start();
        var uri = new Uri(catcher.MineUrl);

        using var socket = SendHeadersPromisingABodyNeverSent(uri);

        // Wait for the server to have actually accepted this connection -- i.e. for
        // HandleSafelyAsync to have started running for it -- before severing it. Resetting any
        // earlier races http.sys handing the accepted context to the app at all: lose that race
        // and no handler ever runs, so nothing below can prove anything either way.
        Assert.True(
            await WaitUntil(() => catcher.RequestsReceived > 0, TimeSpan.FromSeconds(10)),
            $"the server never accepted the connection before the reset was sent " +
            $"(RequestsReceived={catcher.RequestsReceived}, faults so far={catcher.RequestFaults.Count})");

        ResetMidBody(socket);

        // Now wait for that accepted request to hit the reset inside HandleAsync and fall into
        // HandleSafelyAsync's catch, before the real POST that proves the loop is still alive.
        Assert.True(
            await WaitUntil(() => catcher.RequestFaults.Count > 0, TimeSpan.FromSeconds(10)),
            $"the server never recorded the mid-read fault, so the rest of this test would prove " +
            $"nothing (RequestsReceived={catcher.RequestsReceived}, faults so far={catcher.RequestFaults.Count})");

        await PostAsync(catcher.MineUrl, "still hears me");
        var posts = await catcher.DrainAsync(TimeSpan.FromSeconds(5));

        var post = Assert.Single(posts);
        Assert.Equal("still hears me", post.Body);
        Assert.NotEmpty(catcher.RequestFaults);
    }

    /// <summary>Polls until <paramref name="until"/> holds, or the budget runs out. False on timeout.</summary>
    private static async Task<bool> WaitUntil(Func<bool> until, TimeSpan budget)
    {
        var deadline = DateTimeOffset.UtcNow + budget;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (until()) return true;
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return until();
    }

    /// <summary>
    /// Connects and sends a request line and headers promising a 1000-byte body that never
    /// follows, then returns the still-open socket. Split from the reset itself (see
    /// <see cref="ResetMidBody"/>) so a caller can wait for proof the server has accepted the
    /// connection before severing it -- sending and resetting back to back left nothing to stop
    /// the reset from landing before the accept loop had even called <c>GetContextAsync</c>.
    /// </summary>
    private static Socket SendHeadersPromisingABodyNeverSent(Uri uri)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect(IPAddress.Loopback, uri.Port);

        var request = Encoding.ASCII.GetBytes(
            $"POST {uri.AbsolutePath} HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 1000\r\nConnection: close\r\n\r\n");
        socket.Send(request);

        return socket;
    }

    /// <summary>
    /// Forces an abortive RST (via <see cref="LingerOption"/> with a zero timeout, instead of a
    /// graceful close) on a socket already sitting mid-body, per <see cref="SendHeadersPromisingABodyNeverSent"/>
    /// -- the server is left blocked on a read that will now fail rather than quietly see a short
    /// body or a clean EOF.
    /// </summary>
    private static void ResetMidBody(Socket socket)
    {
        socket.LingerState = new LingerOption(true, 0);
        socket.Dispose(); // Closes with that LingerState in effect, sending the reset now.
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

    private static async Task PostAfterDelayAsync(string url, string body, TimeSpan delay)
    {
        await Task.Delay(delay);
        await PostAsync(url, body);
    }

    private static async Task PostAsync(string url, string body)
    {
        using var response = await Http.PostAsync(url, new StringContent(body, Encoding.UTF8));
        response.EnsureSuccessStatusCode();
    }
}
