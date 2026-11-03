using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 3: <see cref="AccountFactsApi"/> over a stub <see cref="HttpMessageHandler"/>. No
/// network. Cookie values are obviously fake. The cookie must leave only as the per-request
/// <c>Cookie</c> header: not in an exception message, a log line, or a returned value.
/// </summary>
public class AccountFactsApiTests
{
    private const string FakeCookie = "FAKE-COOKIE-api-ZZZZ";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> CookieHeaders { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            CookieHeaders.Add(request.Headers.TryGetValues("Cookie", out var v) ? string.Join(";", v) : null);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Lines.Add(formatter(state, exception));
            if (exception is not null) { Lines.Add(exception.ToString()); }
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private static (AccountFactsApi Api, StubHandler Handler, CapturingLogger<AccountFactsApi> Log) Make(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var log = new CapturingLogger<AccountFactsApi>();
        return (new AccountFactsApi(new HttpClient(handler), log), handler, log);
    }

    [Fact]
    public async Task GetRobux_ReadsTheBalance_AndAttachesTheCookiePerRequest()
    {
        var (api, handler, _) = Make(_ => Json(HttpStatusCode.OK, "{\"robux\":1240}"));

        var robux = await api.GetRobuxAsync(FakeCookie, CancellationToken.None);

        Assert.Equal(1240L, robux);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://economy.roblox.com/v1/user/currency", request.RequestUri!.ToString());
        Assert.Equal($".ROBLOSECURITY={FakeCookie}", Assert.Single(handler.CookieHeaders));
    }

    [Fact]
    public async Task GetFriendsOnline_CountsTheDataArray()
    {
        var (api, handler, _) = Make(_ => Json(HttpStatusCode.OK, "{\"data\":[{\"id\":1},{\"id\":2},{\"id\":3}]}"));

        var count = await api.GetFriendsOnlineCountAsync(FakeCookie, 4242, CancellationToken.None);

        Assert.Equal(3, count);
        Assert.Equal("https://friends.roblox.com/v1/users/4242/friends/online", Assert.Single(handler.Requests).RequestUri!.ToString());
    }

    [Fact]
    public async Task GetFriendsOnline_AnEmptyListIsZero_NotUnknown()
    {
        var (api, _, _) = Make(_ => Json(HttpStatusCode.OK, "{\"data\":[]}"));

        Assert.Equal(0, await api.GetFriendsOnlineCountAsync(FakeCookie, 4242, CancellationToken.None));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"unexpected\":true}")]
    [InlineData("[]")]
    public async Task AnUnreadable200_IsUnknown_NotAnException(string body)
    {
        var (api, _, _) = Make(_ => Json(HttpStatusCode.OK, body));

        Assert.Null(await api.GetRobuxAsync(FakeCookie, CancellationToken.None));
        Assert.Null(await api.GetFriendsOnlineCountAsync(FakeCookie, 4242, CancellationToken.None));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ANonSuccess_ThrowsWithTheStatus_AndNeverTheCookie(HttpStatusCode status)
    {
        var (api, _, log) = Make(_ => Json(status, "{\"errors\":[{\"message\":\"nope\"}]}"));

        var robux = await Assert.ThrowsAsync<AccountFactsRequestException>(() => api.GetRobuxAsync(FakeCookie, CancellationToken.None));
        var friends = await Assert.ThrowsAsync<AccountFactsRequestException>(() => api.GetFriendsOnlineCountAsync(FakeCookie, 4242, CancellationToken.None));

        Assert.Equal((int)status, robux.StatusCode);
        Assert.Equal((int)status, friends.StatusCode);
        foreach (var text in new[] { robux.Message, robux.ToString(), friends.Message, friends.ToString() }.Concat(log.Lines))
        {
            Assert.DoesNotContain(FakeCookie, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetMembership_ReturnsTheRawMeasurement_WithoutInterpretingIt()
    {
        var (api, handler, _) = Make(_ => Json(HttpStatusCode.OK, "true"));

        var probe = await api.GetMembershipAsync(FakeCookie, 4242, CancellationToken.None);

        Assert.Equal(new MembershipProbe(200, "true"), probe);
        Assert.Equal("https://premiumfeatures.roblox.com/v1/users/4242/validate-membership", Assert.Single(handler.Requests).RequestUri!.ToString());
        Assert.Equal($".ROBLOSECURITY={FakeCookie}", Assert.Single(handler.CookieHeaders));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task GetMembership_DoesNotThrowOnAStatus_ItReportsIt(HttpStatusCode status)
    {
        var (api, _, _) = Make(_ => Json(status, "gone"));

        var probe = await api.GetMembershipAsync(FakeCookie, 4242, CancellationToken.None);

        Assert.Equal((int)status, probe.Status);
        Assert.Equal("gone", probe.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.MovedPermanently)]
    public async Task ARedirect_IsJustAStatus_NeverFollowed(HttpStatusCode status)
    {
        // The registration disables auto-redirect (AccountFactsClient_PrimaryHandler_DoesNotFollowRedirects),
        // so a 3xx reaches the client as a 3xx and maps like any other non-success.
        var (api, _, _) = Make(_ => new HttpResponseMessage(status));

        var ex = await Assert.ThrowsAsync<AccountFactsRequestException>(() => api.GetRobuxAsync(FakeCookie, CancellationToken.None));
        Assert.Equal((int)status, ex.StatusCode);
        Assert.Equal((int)status, (await api.GetMembershipAsync(FakeCookie, 4242, CancellationToken.None)).Status);
    }

    [Theory]
    [InlineData("FAKE-COOKIE-a\r\nX-Injected: 1")]
    [InlineData("FAKE-COOKIE-a\nb")]
    [InlineData("FAKE-COOKIE-a\0b")]
    [InlineData("FAKE-COOKIE-a\tb")]
    public async Task AControlCharacterCookie_IsRefusedBeforeAnyRequest_WithNoValueInTheMessage(string cookie)
    {
        var (api, handler, log) = Make(_ => Json(HttpStatusCode.OK, "{}"));

        var ex1 = await Assert.ThrowsAsync<ArgumentException>(() => api.GetRobuxAsync(cookie, CancellationToken.None));
        var ex2 = await Assert.ThrowsAsync<ArgumentException>(() => api.GetFriendsOnlineCountAsync(cookie, 4242, CancellationToken.None));
        var ex3 = await Assert.ThrowsAsync<ArgumentException>(() => api.GetMembershipAsync(cookie, 4242, CancellationToken.None));

        Assert.Empty(handler.Requests);
        foreach (var text in new[] { ex1.Message, ex1.ToString(), ex2.Message, ex3.Message }.Concat(log.Lines))
        {
            Assert.DoesNotContain("FAKE-COOKIE", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetMembership_CapsTheBodyItKeeps()
    {
        var (api, _, _) = Make(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 5000)) });

        var probe = await api.GetMembershipAsync(FakeCookie, 4242, CancellationToken.None);

        Assert.Equal(AccountFactsApi.MaxProbeBodyChars, probe.Body!.Length);
    }

    [Fact]
    public async Task EveryCall_LogsOnlyTheEndpointNameAndStatus()
    {
        var (api, _, log) = Make(_ => Json(HttpStatusCode.OK, "{\"robux\":5,\"data\":[]}"));

        await api.GetRobuxAsync(FakeCookie, CancellationToken.None);
        await api.GetFriendsOnlineCountAsync(FakeCookie, 987654321, CancellationToken.None);
        await api.GetMembershipAsync(FakeCookie, 987654321, CancellationToken.None);

        Assert.Equal(3, log.Lines.Count);
        Assert.All(log.Lines, line =>
        {
            Assert.Contains("AccountFactsApi", line, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeCookie, line, StringComparison.Ordinal);
            Assert.DoesNotContain("987654321", line, StringComparison.Ordinal); // a user id is an identity
            Assert.DoesNotContain("http", line, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task ArgumentErrors_NameTheParameter_NotTheValue()
    {
        var (api, handler, _) = Make(_ => Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<ArgumentException>(() => api.GetRobuxAsync("", CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetFriendsOnlineCountAsync(FakeCookie, 0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetMembershipAsync(FakeCookie, -5, CancellationToken.None));
        Assert.Empty(handler.Requests); // rejected before anything left the machine
    }

    [Fact]
    public void TheConstructor_SetsTheRororoUserAgent_WhenTheRegistrationDidNot()
    {
        var client = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));

        _ = new AccountFactsApi(client, NullLogger<AccountFactsApi>.Instance);

        var product = Assert.Single(client.DefaultRequestHeaders.UserAgent);
        Assert.Equal("RORORO", product.Product?.Name);
    }
}
