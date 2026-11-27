using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Feeds;

namespace ROROROblox.Tests.Feeds;

/// <summary>
/// <see cref="RobloxStatusApi"/> over a stub handler: no network. Every failure maps to a
/// <see cref="FeedOutcome"/> and nothing throws. Hand-written bodies are named <c>Synthetic*</c> and exist
/// only for what no real answer can show: a missing code, an oversized body, a non-hex id.
/// </summary>
public class RobloxStatusApiTests
{
    private const string ComponentId = "5f31ce9f6f386704befc1f81";
    private const string ContainerId = "5f31cdef5f2c7c04bbdc4fbf";

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

    /// <summary>A body with no declared length, so the cap has to hold while streaming.</summary>
    private sealed class UnsizedContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes, 0, bytes.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
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

    private static RobloxStatusApi Api(HttpMessageHandler handler, CapturingLogger<RobloxStatusApi>? log = null) =>
        new(new HttpClient(handler), log ?? new CapturingLogger<RobloxStatusApi>());

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Body(string cells) =>
        "{\"result\":{\"status\":[{\"id\":\"" + ComponentId + "\",\"containers\":[" + cells + "]}]}}";

    private static string Cell(string id, string codeJson) =>
        "{\"id\":\"" + id + "\"" + (codeJson.Length == 0 ? "" : ",\"status_code\":" + codeJson) + "}";

    // ---- the happy path ----

    [Fact]
    public async Task RealAnswer_IsOk_WithOneGet_AndNoCookie()
    {
        var bytes = RobloxStatusEvaluatorTests.FixtureBytes("status-roblox-operational-2026-11-26.json");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

        var read = await Api(handler).ReadAsync();

        Assert.Equal(FeedOutcome.Ok, read.Outcome);
        Assert.NotNull(read.Snapshot);
        Assert.NotEmpty(read.Snapshot!.Cells);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(RobloxStatusApi.StatusUrl, request.RequestUri!.AbsoluteUri);
        Assert.False(request.Headers.Contains("Cookie"));
        Assert.Null(request.Content);
    }

    [Fact]
    public async Task EmptyStatusList_IsOk_WithNoCells()
    {
        var read = await Api(new StubHandler(_ => Json("{\"result\":{\"status\":[]}}"))).ReadAsync();

        Assert.Equal(FeedOutcome.Ok, read.Outcome);
        Assert.Empty(read.Snapshot!.Cells);
    }

    // ---- each failure maps to its outcome, and none throws ----

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, FeedOutcome.Refused)]
    [InlineData(HttpStatusCode.TooManyRequests, FeedOutcome.Refused)]
    [InlineData(HttpStatusCode.InternalServerError, FeedOutcome.HttpError)]
    [InlineData(HttpStatusCode.NotFound, FeedOutcome.HttpError)]
    [InlineData(HttpStatusCode.Found, FeedOutcome.HttpError)]
    public async Task NonSuccessStatus_MapsToItsOutcome(HttpStatusCode status, FeedOutcome expected)
    {
        var read = await Api(new StubHandler(_ => Json("{}", status))).ReadAsync();

        Assert.Equal(expected, read.Outcome);
        Assert.Null(read.Snapshot);
    }

    [Fact]
    public async Task NoNetwork_IsNetwork()
    {
        var read = await Api(new ThrowingHandler(new HttpRequestException("name not resolved"))).ReadAsync();

        Assert.Equal(FeedOutcome.Network, read.Outcome);
        Assert.Null(read.Snapshot);
    }

    [Fact]
    public async Task AnUnexpectedException_IsNetwork_AndDoesNotThrow()
    {
        var read = await Api(new ThrowingHandler(new InvalidOperationException("boom"))).ReadAsync();

        Assert.Equal(FeedOutcome.Network, read.Outcome);
    }

    [Fact]
    public async Task ATimeout_IsTimeout()
    {
        var read = await Api(new ThrowingHandler(new TaskCanceledException("timed out"))).ReadAsync();

        Assert.Equal(FeedOutcome.Timeout, read.Outcome);
        Assert.Null(read.Snapshot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>not json</html>")]
    [InlineData("{}")]
    [InlineData("{\"result\":{}}")]
    [InlineData("{\"result\":{\"status\":\"Operational\"}}")]
    [InlineData("[1,2,3]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public async Task EmptyNotJsonOrWrongShape_IsShape(string body)
    {
        var read = await Api(new StubHandler(_ => Json(body))).ReadAsync();

        Assert.Equal(FeedOutcome.Shape, read.Outcome);
        Assert.Null(read.Snapshot);
    }

    [Fact]
    public async Task OversizedBody_WithADeclaredLength_IsShape()
    {
        var big = new byte[RobloxStatusApi.MaxBodyBytes + 1];
        var read = await Api(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(big) })).ReadAsync();

        Assert.Equal(FeedOutcome.Shape, read.Outcome);
    }

    [Fact]
    public async Task OversizedBody_WithNoDeclaredLength_IsShape()
    {
        var padded = Encoding.UTF8.GetBytes(Body(Cell(ContainerId, "100")) + new string(' ', RobloxStatusApi.MaxBodyBytes));
        var read = await Api(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnsizedContent(padded) })).ReadAsync();

        Assert.Equal(FeedOutcome.Shape, read.Outcome);
    }

    [Fact]
    public async Task ABodyExactlyAtTheCap_IsRead()
    {
        var json = Body(Cell(ContainerId, "100"));
        var padded = Encoding.UTF8.GetBytes(json + new string(' ', RobloxStatusApi.MaxBodyBytes - json.Length));
        Assert.Equal(RobloxStatusApi.MaxBodyBytes, padded.Length);

        var read = await Api(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new UnsizedContent(padded) })).ReadAsync();

        Assert.Equal(FeedOutcome.Ok, read.Outcome);
    }

    // ---- what is kept ----

    [Fact]
    public async Task SyntheticMissingCode_DropsThatCell()
    {
        var read = await Api(new StubHandler(_ => Json(Body(Cell(ContainerId, "") + "," + Cell("60047c5629a6230534c2300a", "300"))))).ReadAsync();

        var cell = Assert.Single(read.Snapshot!.Cells);
        Assert.Equal("60047c5629a6230534c2300a", cell.ContainerId);
        Assert.Equal(300, cell.Code);
    }

    [Fact]
    public async Task SyntheticNullCode_DropsThatCell()
    {
        var read = await Api(new StubHandler(_ => Json(Body(Cell(ContainerId, "null"))))).ReadAsync();

        Assert.Empty(read.Snapshot!.Cells);
    }

    [Theory]
    [InlineData("not-a-hex-id-at-all-xxxx")]
    [InlineData("5f31cdef5f2c7c04bbdc4fb")]
    [InlineData("5f31cdef5f2c7c04bbdc4fbfa")]
    [InlineData("5f31cdef5f2c7c04bbdc4fbg")]
    [InlineData("")]
    public async Task SyntheticBadContainerId_DropsThatCell(string id)
    {
        var read = await Api(new StubHandler(_ => Json(Body(Cell(id, "300"))))).ReadAsync();

        Assert.Equal(FeedOutcome.Ok, read.Outcome);
        Assert.Empty(read.Snapshot!.Cells);
    }

    [Fact]
    public async Task SyntheticBadComponentId_DropsItsCells()
    {
        var json = "{\"result\":{\"status\":[{\"id\":\"zzzz\",\"containers\":[" + Cell(ContainerId, "300") + "]}]}}";

        var read = await Api(new StubHandler(_ => Json(json))).ReadAsync();

        Assert.Empty(read.Snapshot!.Cells);
    }

    [Fact]
    public async Task UppercaseHexIds_AreAccepted()
    {
        var read = await Api(new StubHandler(_ => Json(Body(Cell(ContainerId.ToUpperInvariant(), "300"))))).ReadAsync();

        Assert.Single(read.Snapshot!.Cells);
    }

    // ---- no third-party text ----

    [Fact]
    public async Task NoPageTextReachesTheLog()
    {
        const string marker = "ZXQ-PAGE-TEXT-MARKER";
        var json = "{\"result\":{\"status_overall\":{\"status\":\"" + marker + "\"},\"status\":[{\"id\":\"" + ComponentId +
                   "\",\"name\":\"" + marker + "\",\"containers\":[{\"id\":\"" + ContainerId + "\",\"name\":\"" + marker +
                   "\",\"status\":\"" + marker + "\",\"status_code\":300}]}],\"incidents\":[{\"name\":\"" + marker + "\"}]}}";
        var log = new CapturingLogger<RobloxStatusApi>();

        var read = await Api(new StubHandler(_ => Json(json)), log).ReadAsync();

        Assert.Equal(FeedOutcome.Ok, read.Outcome);
        Assert.NotEmpty(log.Lines);
        Assert.DoesNotContain(log.Lines, l => l.Contains(marker));
    }

    [Fact]
    public async Task FailureLogsCarryTheOutcomeAndCodeOnly()
    {
        var log = new CapturingLogger<RobloxStatusApi>();

        await Api(new StubHandler(_ => Json("secret body ZXQ", HttpStatusCode.TooManyRequests)), log).ReadAsync();

        var line = Assert.Single(log.Lines);
        Assert.Contains("Refused", line);
        Assert.Contains("429", line);
        Assert.DoesNotContain("ZXQ", line);
    }

    private static IEnumerable<Type> StatusDtos() =>
        typeof(RobloxStatusApi).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public)
            .Where(t => t.IsSealed && t.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null);

    private static IEnumerable<string> StringMembers(Type t) =>
        t.GetProperties(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .Concat(t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.FieldType == typeof(string) && !f.Name.EndsWith("k__BackingField"))
                .Select(f => f.Name));

    [Fact]
    public void NoDto_HasAStringOtherThanId()
    {
        var dtos = StatusDtos().ToList();
        Assert.True(dtos.Count >= 4, "vacuity floor: the four status DTOs must be found by reflection");

        foreach (var dto in dtos)
        {
            Assert.All(StringMembers(dto), name => Assert.True(name == "Id", $"{dto.Name}.{name} is a string; only Id may be."));
            Assert.All(dto.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SelectMany(c => c.GetParameters()).Where(p => p.ParameterType == typeof(string)),
                p => Assert.True(p.Name == "Id", $"{dto.Name} takes a string parameter {p.Name}; only Id may be."));
        }
    }

    [Fact]
    public void SnapshotCellAndReading_HoldNoString_ExceptTheTwoIdsOfACell()
    {
        Assert.Empty(StringMembers(typeof(RobloxStatusSnapshot)));
        Assert.Empty(StringMembers(typeof(RobloxStatusReading)));
        Assert.Equal(["ComponentId", "ContainerId"], StringMembers(typeof(StatusCell)).OrderBy(n => n));
    }
}
