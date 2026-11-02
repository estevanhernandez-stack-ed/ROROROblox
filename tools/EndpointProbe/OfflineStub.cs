using System.Net;
using System.Text;

namespace ROROROblox.EndpointProbe;

/// <summary>
/// The handler <c>--offline</c> swaps in: canned responses, no socket, no request ever leaves the
/// process. It exists so the report path can be exercised end to end without touching roblox.com
/// (CLAUDE.md: no end-to-end automation against the real site; the live measurements are the
/// builder's, at checkpoint C1).
/// <para>
/// The canned script mirrors the shape of a captcha-prone account: the first ticket POST is the CSRF
/// handshake (403 with <c>x-csrf-token</c>), the second is a 403 carrying
/// <c>rblx-challenge-type: captcha</c>, an id and base64 metadata that contains a user id (so the
/// redaction has something to remove), then 200 for presence and for membership. Every response also
/// carries a <c>Set-Cookie</c> and the handshake a token, none of which may reach the report.
/// </para>
/// </summary>
public sealed class OfflineStub : HttpMessageHandler
{
    /// <summary>Values the stub emits that must never appear in output. The self-test greps for them.</summary>
    public const string StubCsrfToken = "OFFLINE-STUB-CSRF-TOKEN";
    public const string StubSetCookie = "OFFLINE-STUB-SET-COOKIE-VALUE";

    private readonly long _userId;
    private int _ticketPosts;

    public OfflineStub(long? userId) => _userId = userId ?? 1234567890L;

    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        var url = request.RequestUri?.ToString() ?? string.Empty;

        // A probe that forgot the cookie would be measuring a different request than the client sends.
        if (!request.Headers.Contains("Cookie"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("offline stub: request carried no Cookie header"),
            });
        }

        HttpResponseMessage response;
        if (url.StartsWith(Prober.AuthTicketEndpoint, StringComparison.Ordinal))
        {
            _ticketPosts++;
            response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.TryAddWithoutValidation("x-csrf-token", StubCsrfToken);
            if (_ticketPosts >= 2)
            {
                var metadata = Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    $"{{\"unifiedCaptchaId\":\"offline-stub\",\"userId\":\"{_userId}\",\"actionType\":\"Generic\"}}"));
                response.Headers.TryAddWithoutValidation("rblx-challenge-type", "captcha");
                response.Headers.TryAddWithoutValidation("rblx-challenge-id", "offline-stub-challenge-id");
                response.Headers.TryAddWithoutValidation("rblx-challenge-metadata", metadata);
                response.Content = new StringContent("{\"errors\":[{\"code\":0,\"message\":\"Challenge is required to authorize the request\"}]}",
                    Encoding.UTF8, "application/json");
            }
            else
            {
                response.Content = new StringContent("{\"errors\":[{\"code\":0,\"message\":\"Token Validation Failed\"}]}",
                    Encoding.UTF8, "application/json");
            }
        }
        else if (url.StartsWith(Prober.PresenceEndpoint, StringComparison.Ordinal))
        {
            response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"{{\"userPresences\":[{{\"userPresenceType\":2,\"lastLocation\":\"Offline Stub Game\",\"placeId\":1,\"rootPlaceId\":1,\"gameId\":\"00000000-0000-0000-0000-000000000000\",\"universeId\":1,\"userId\":{_userId},\"lastOnline\":\"2026-11-01T00:00:00Z\"}}]}}",
                    Encoding.UTF8, "application/json"),
            };
        }
        else if (url.Contains("/validate-membership", StringComparison.Ordinal))
        {
            response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("true", Encoding.UTF8, "application/json"),
            };
        }
        else
        {
            response = new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        response.Headers.TryAddWithoutValidation("Set-Cookie", $"stub={StubSetCookie}; path=/");
        return Task.FromResult(response);
    }
}
