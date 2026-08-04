namespace ROROROblox.Tests.Discord;

/// <summary>
/// Canned HTTP for the webhook suites — records every request body so a test can assert what
/// actually went over the wire, and how many times. Shared rather than per-suite: two copies of a
/// transport double is two chances for them to disagree about what the transport does.
/// </summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<string> Bodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is not null)
        {
            Bodies.Add(await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }

        return respond(request);
    }
}
