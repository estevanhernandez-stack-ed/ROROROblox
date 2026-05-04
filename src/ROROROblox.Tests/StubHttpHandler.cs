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

    public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responders { get; } = new();

    public void EnqueueResponse(HttpResponseMessage response)
    {
        Responders.Enqueue(_ => response);
    }

    public void EnqueueResponse(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        Responders.Enqueue(responder);
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Responders.Count == 0)
        {
            throw new InvalidOperationException(
                $"StubHttpHandler ran out of scripted responses on request {Requests.Count} ({request.Method} {request.RequestUri}).");
        }
        var responder = Responders.Dequeue();
        return Task.FromResult(responder(request));
    }
}
