using System.Net;
using System.Net.Http;
using ROROROblox.App.Localization;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Launch errors in plain language (2026-11-10). A network drop, a timeout and Roblox asking the
/// app to slow down all used to land on the account's row as "Failed to obtain auth ticket:"
/// followed by a raw exception message, in English, inside a translated interface. Core now says
/// WHICH of those happened, and the App has a sentence for each. The raw text still goes to the log.
/// </summary>
public class PlainLaunchErrorTests
{
    // === Core says which thing went wrong ===

    [Fact]
    public void ANetworkFailure_IsNoConnection()
    {
        Assert.Equal(LaunchFailureKind.NoConnection,
            RobloxLauncher.ClassifyTicketFailure(new HttpRequestException("No such host is known.")));
    }

    [Fact]
    public void AnHttpClientTimeout_IsTimedOut()
    {
        // HttpClient reports its timeout as a TaskCanceledException wrapping a TimeoutException.
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException());

        Assert.Equal(LaunchFailureKind.TimedOut, RobloxLauncher.ClassifyTicketFailure(timeout));
        Assert.Equal(LaunchFailureKind.TimedOut, RobloxLauncher.ClassifyTicketFailure(new TimeoutException()));
    }

    [Fact]
    public void A429_IsRateLimited()
    {
        Assert.Equal(LaunchFailureKind.RateLimited,
            RobloxLauncher.ClassifyTicketFailure(new RobloxApiStatusException(429, "Roblox auth-ticket endpoint returned 429.")));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    public void AServerError_IsRobloxUnavailable(int status)
    {
        Assert.Equal(LaunchFailureKind.RobloxUnavailable,
            RobloxLauncher.ClassifyTicketFailure(new RobloxApiStatusException(status, "x")));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    public void AnyOtherStatus_StaysTheGenericKind_BecauseNobodyKnowsWhatItMeansYet(int status)
    {
        Assert.Equal(LaunchFailureKind.AuthTicketFailed,
            RobloxLauncher.ClassifyTicketFailure(new RobloxApiStatusException(status, "x")));
    }

    [Fact]
    public void AnythingUnrecognised_StaysTheGenericKind()
    {
        Assert.Equal(LaunchFailureKind.AuthTicketFailed,
            RobloxLauncher.ClassifyTicketFailure(new InvalidOperationException("Auth ticket response missing RBX-Authentication-Ticket header.")));
    }

    // === The API tells a status apart from everything else ===

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TheApi_ReportsAFailedStatusOnTheTicketExchange_WithItsCode(HttpStatusCode status)
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(WithHeader(new HttpResponseMessage(HttpStatusCode.Forbidden), "x-csrf-token", "csrf"));
        stub.EnqueueResponse(new HttpResponseMessage(status));
        var api = new RobloxApi(new HttpClient(stub));

        var thrown = await Assert.ThrowsAsync<RobloxApiStatusException>(() => api.GetAuthTicketAsync("FAKE_COOKIE_FOR_TESTS_ONLY"));

        Assert.Equal((int)status, thrown.StatusCode);
        // Still an InvalidOperationException, so nothing that caught the old type stops catching it.
        Assert.IsAssignableFrom<InvalidOperationException>(thrown);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task TheApi_ReportsAFailedStatusOnTheFirstRequest_WithItsCode(HttpStatusCode status)
    {
        // The first request normally answers 403 with a token. A 429 or a 5xx there carries no
        // token, and used to surface as "did not return X-CSRF-TOKEN", which hid what happened.
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(new HttpResponseMessage(status));
        var api = new RobloxApi(new HttpClient(stub));

        var thrown = await Assert.ThrowsAsync<RobloxApiStatusException>(() => api.GetAuthTicketAsync("FAKE_COOKIE_FOR_TESTS_ONLY"));

        Assert.Equal((int)status, thrown.StatusCode);
    }

    // === The App has a sentence for each, and it reads like one ===

    [Fact]
    public void EveryLaunchFailureKind_HasASentence()
    {
        foreach (var kind in Enum.GetValues<LaunchFailureKind>())
        {
            var sentence = CoreMessageCatalog.For(new LaunchResult.Failed(kind, "raw detail text"));
            Assert.False(string.IsNullOrWhiteSpace(sentence), $"{kind} has no sentence");
        }
    }

    [Theory]
    [InlineData(LaunchFailureKind.NoConnection)]
    [InlineData(LaunchFailureKind.TimedOut)]
    [InlineData(LaunchFailureKind.RateLimited)]
    [InlineData(LaunchFailureKind.RobloxUnavailable)]
    public void TheNamedFailures_NeverShowTheRawError(LaunchFailureKind kind)
    {
        var sentence = CoreMessageCatalog.For(new LaunchResult.Failed(kind, "System.Net.Sockets.SocketException (11001): No such host is known."));

        Assert.DoesNotContain("SocketException", sentence);
        Assert.DoesNotContain("No such host", sentence);
    }

    [Fact]
    public void NoLaunchSentence_UsesTheWordsOnlyADeveloperWouldKnow()
    {
        foreach (var kind in Enum.GetValues<LaunchFailureKind>())
        {
            var sentence = CoreMessageCatalog.For(new LaunchResult.Failed(kind, string.Empty));
            foreach (var jargon in new[] { "auth ticket", "Process.Start", "endpoint", "explicit target", "CSRF" })
            {
                Assert.DoesNotContain(jargon, sentence, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void TheGenericFailure_KeepsItsDetail_BecauseABugReportNeedsIt()
    {
        var sentence = CoreMessageCatalog.For(new LaunchResult.Failed(LaunchFailureKind.AuthTicketFailed, "Roblox auth-ticket endpoint returned 400."));

        Assert.Contains("returned 400", sentence);
    }

    private static HttpResponseMessage WithHeader(HttpResponseMessage response, string name, string value)
    {
        response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }
}
