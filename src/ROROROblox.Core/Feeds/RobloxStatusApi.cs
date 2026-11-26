using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ROROROblox.Core.Feeds;

/// <summary>
/// Reads Roblox's public Status.io answer and keeps ids and integers only. The DTOs have no text
/// property, so nothing the page says can reach the window or the log; the log line carries the outcome
/// and the HTTP code. ONE public constructor, as the typed-client activator requires
/// (<c>TypedHttpClientRegistrationTests</c>).
/// </summary>
public sealed class RobloxStatusApi : IRobloxStatusApi
{
    public const string StatusUrl = "https://api.status.io/1.0/status/59db90dbcdeb2f04dadcf16d";

    /// <summary>512 KB: the largest archived answer from any page is 44 KB.</summary>
    public const int MaxBodyBytes = 512 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ILogger<RobloxStatusApi> _log;

    public RobloxStatusApi(HttpClient http, ILogger<RobloxStatusApi> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    private sealed record Envelope(Result? Result);

    private sealed record Result(Component[]? Status);

    private sealed record Component(string? Id, Container[]? Containers);

    private sealed record Container(string? Id, [property: JsonPropertyName("status_code")] int? StatusCode);

    public async Task<RobloxStatusRead> ReadAsync(CancellationToken ct = default)
    {
        FeedOutcome outcome;
        int? httpCode = null;
        RobloxStatusSnapshot? snapshot = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            using var response = await _http
                .GetAsync(StatusUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            httpCode = (int)response.StatusCode;

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                outcome = FeedOutcome.Refused;
            }
            else if (!response.IsSuccessStatusCode)
            {
                outcome = FeedOutcome.HttpError;
            }
            else
            {
                var body = await ReadLimitedAsync(response, cts.Token).ConfigureAwait(false);
                snapshot = body is null ? null : Parse(body);
                outcome = snapshot is null ? FeedOutcome.Shape : FeedOutcome.Ok;
            }
        }
        catch (OperationCanceledException)
        {
            outcome = FeedOutcome.Timeout;
        }
        catch (Exception)
        {
            outcome = FeedOutcome.Network;
        }

        _log.LogDebug("roblox-status read: {Outcome} (HTTP {Code})", outcome, httpCode?.ToString() ?? "none");
        return new RobloxStatusRead(outcome, outcome == FeedOutcome.Ok ? snapshot : null);
    }

    /// <summary>The body, or null when it is larger than the cap. Streams, so an oversized body is never buffered whole.</summary>
    private static async Task<byte[]?> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is long declared && declared > MaxBodyBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Null when the body is not the expected shape. A cell is kept only with two 24-hex ids and a code.</summary>
    internal static RobloxStatusSnapshot? Parse(byte[] body)
    {
        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(body, Json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (envelope?.Result?.Status is not { } components)
        {
            return null;
        }

        var cells = new List<StatusCell>();
        foreach (var component in components)
        {
            if (component is null || !IsId(component.Id) || component.Containers is not { } containers)
            {
                continue;
            }

            foreach (var container in containers)
            {
                if (container is { StatusCode: int code } && IsId(container.Id))
                {
                    cells.Add(new StatusCell(component.Id!, container.Id!, code));
                }
            }
        }

        return new RobloxStatusSnapshot(cells);
    }

    private static bool IsId(string? id)
    {
        if (id is not { Length: 24 })
        {
            return false;
        }

        foreach (var c in id)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
