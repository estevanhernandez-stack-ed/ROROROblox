namespace ROROROblox.Tests;

/// <summary>
/// Minimal stub <see cref="HttpMessageHandler"/> for unit-testing HTTP-bound services.
/// Each test scripts a queue of responder functions and inspects the captured request log
/// after the call. No live network — Roblox-side calls happen only in the manual smoke
/// checklist before each release tag (per spec §8).
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>
    /// Bodies captured at Send time. Index-aligned with <see cref="Requests"/>. Use this instead
    /// of <c>Requests[i].Content.ReadAsStringAsync()</c> — the production code disposes its
    /// HttpContent (via <c>using</c>) before the test gets a chance to read it.
    /// </summary>
    public List<string> RequestBodies { get; } = [];

    public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responders { get; } = new();

    public void EnqueueResponse(HttpResponseMessage response)
    {
        Responders.Enqueue(_ => response);
    }

    public void EnqueueResponse(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        Responders.Enqueue(responder);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (request.Content is not null)
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        else
        {
            RequestBodies.Add(string.Empty);
        }
        if (Responders.Count == 0)
        {
            throw new InvalidOperationException(
                $"StubHttpHandler ran out of scripted responses on request {Requests.Count} ({request.Method} {request.RequestUri}).");
        }
        var responder = Responders.Dequeue();
        return responder(request);
    }
}
