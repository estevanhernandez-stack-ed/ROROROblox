using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.Feeds;
using ROROROblox.Core.Library;

namespace ROROROblox.Tests.Library;

/// <summary><see cref="GamePulseApi"/> over a stub handler: no network, nothing throws on a bad answer.</summary>
public class GamePulseApiTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class ThrowingHandler(Exception ex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw ex;
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (GamePulseApi Api, StubHandler Handler) Make(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new GamePulseApi(new HttpClient(handler), NullLogger<GamePulseApi>.Instance), handler);
    }

    private static long[] Ids(int n) => Enumerable.Range(1, n).Select(i => (long)i).ToArray();

    [Fact]
    public async Task FiftyIds_AreOneRequest()
    {
        var (api, handler) = Make(_ => Json("""{"data":[]}"""));

        var read = await api.ReadAsync(Ids(50));

        Assert.Equal(FeedOutcome.Ok, read.Outcome);
        var request = Assert.Single(handler.Requests);
        Assert.StartsWith(GamePulseApi.BaseUrl, request.RequestUri!.ToString());
        Assert.Equal(50, request.RequestUri.Query.Split('=')[1].Split(',').Length);
    }

    [Fact]
    public async Task FiftyOneIds_Throws_BeforeAnyRequest()
    {
        var (api, handler) = Make(_ => Json("""{"data":[]}"""));

        await Assert.ThrowsAsync<ArgumentException>(() => api.ReadAsync(Ids(51)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Request_CarriesNoCookieAndNoAuthorization()
    {
        var (api, handler) = Make(_ => Json("""{"data":[]}"""));

        await api.ReadAsync([1, 2]);

        var request = Assert.Single(handler.Requests);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task GoodAnswer_ReadsCountAndDate_AndAbsentGameIsNotFound()
    {
        var (api, _) = Make(_ => Json("""
            {"data":[{"id":1,"playing":81827,"updated":"2026-11-24T10:00:00.123Z","name":"ignored"}]}
            """));

        var read = await api.ReadAsync([1, 2]);

        Assert.Equal(FeedOutcome.Ok, read.Outcome);
        var found = Assert.Single(read.Answer!.Found);
        Assert.Equal(1, found.Key);
        Assert.Equal(81827, found.Value.Playing);
        Assert.Equal(new DateTimeOffset(2026, 11, 24, 10, 0, 0, 123, TimeSpan.Zero), found.Value.UpdatedUtc);
    }

    [Fact]
    public async Task OkWithoutData_IsShape_NotEveryGameGone()
    {
        var (api, _) = Make(_ => Json("""{"nodata":[]}"""));

        var read = await api.ReadAsync([1]);

        Assert.Equal(FeedOutcome.Shape, read.Outcome);
        Assert.Null(read.Answer);
    }

    [Fact]
    public async Task OutOfRangeCount_IsDroppedForThatGameAlone()
    {
        var (api, _) = Make(_ => Json("""
            {"data":[{"id":1,"playing":100000001,"updated":"2026-11-24T10:00:00Z"},
                     {"id":2,"playing":-1,"updated":"2026-11-24T10:00:00Z"},
                     {"id":3,"playing":5,"updated":"2026-11-24T10:00:00Z"}]}
            """));

        var read = await api.ReadAsync([1, 2, 3]);

        var found = Assert.Single(read.Answer!.Found);
        Assert.Equal(3, found.Key);
    }

    [Fact]
    public async Task OutOfRangeDate_IsDroppedForThatGameAlone()
    {
        var future = DateTimeOffset.UtcNow.AddDays(3).ToString("o");
        var (api, _) = Make(_ => Json($$"""
            {"data":[{"id":1,"playing":10,"updated":"2005-12-31T00:00:00Z"},
                     {"id":2,"playing":20,"updated":"{{future}}"},
                     {"id":3,"playing":30,"updated":"2026-11-24T10:00:00Z"}]}
            """));

        var read = await api.ReadAsync([1, 2, 3]);

        var found = read.Answer!.Found;
        Assert.Null(found[1].UpdatedUtc);
        Assert.Equal(10, found[1].Playing);
        Assert.Null(found[2].UpdatedUtc);
        Assert.NotNull(found[3].UpdatedUtc);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, FeedOutcome.Refused)]
    [InlineData(HttpStatusCode.TooManyRequests, FeedOutcome.Refused)]
    [InlineData(HttpStatusCode.InternalServerError, FeedOutcome.HttpError)]
    [InlineData(HttpStatusCode.BadRequest, FeedOutcome.HttpError)]
    public async Task NonSuccess_MapsToAnOutcome(HttpStatusCode code, FeedOutcome expected)
    {
        var (api, _) = Make(_ => Json("{}", code));

        var read = await api.ReadAsync([1]);

        Assert.Equal(expected, read.Outcome);
        Assert.Null(read.Answer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    public async Task EmptyNotJsonOrWrongShape_IsShape(string body)
    {
        var (api, _) = Make(_ => Json(body));

        var read = await api.ReadAsync([1]);

        Assert.Equal(FeedOutcome.Shape, read.Outcome);
    }

    [Fact]
    public async Task NetworkFailure_IsNetwork_AndDoesNotThrow()
    {
        var api = new GamePulseApi(new HttpClient(new ThrowingHandler(new HttpRequestException("dns"))), NullLogger<GamePulseApi>.Instance);

        var read = await api.ReadAsync([1]);

        Assert.Equal(FeedOutcome.Network, read.Outcome);
    }

    [Fact]
    public async Task Timeout_IsTimeout()
    {
        var api = new GamePulseApi(new HttpClient(new ThrowingHandler(new TaskCanceledException())), NullLogger<GamePulseApi>.Instance);

        var read = await api.ReadAsync([1]);

        Assert.Equal(FeedOutcome.Timeout, read.Outcome);
    }
}
