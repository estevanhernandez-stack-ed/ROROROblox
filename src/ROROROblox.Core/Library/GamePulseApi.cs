using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ROROROblox.Core.Feeds;

namespace ROROROblox.Core.Library;

/// <summary>
/// Reads <c>games.roblox.com/v1/games</c> for up to 50 universe ids in one anonymous request. The DTO
/// reads <c>id</c>, <c>playing</c> and <c>updated</c> only. ONE public constructor, as the typed-client
/// activator requires (<c>TypedHttpClientRegistrationTests</c>).
/// </summary>
public sealed class GamePulseApi : IGamePulseApi
{
    public const string BaseUrl = "https://games.roblox.com/v1/games?universeIds=";

    public const int MaxPlaying = 100_000_000;

    /// <summary>
    /// 1 MB. A live five-game answer measured 7,075 bytes on 2026-11-27 (about 1.4 KB a game, most of it
    /// the description we never read), so a full 50-id batch is near 70 KB.
    /// </summary>
    public const int MaxBodyBytes = 1024 * 1024;

    private static readonly DateTimeOffset EarliestUpdated = new(2006, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly ILogger<GamePulseApi> _log;

    public GamePulseApi(HttpClient http, ILogger<GamePulseApi> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    private sealed record Envelope(Item?[]? Data);

    private sealed record Item(long? Id, long? Playing, string? Updated);

    public Task<GamePulseRead> ReadAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(universeIds);
        if (universeIds.Count > IGamePulseApi.MaxIdsPerRequest)
        {
            throw new ArgumentException(
                $"At most {IGamePulseApi.MaxIdsPerRequest} universe ids per request; got {universeIds.Count}.",
                nameof(universeIds));
        }

        return ReadCoreAsync(universeIds, ct);
    }

    private async Task<GamePulseRead> ReadCoreAsync(IReadOnlyCollection<long> universeIds, CancellationToken ct)
    {
        if (universeIds.Count == 0)
        {
            return new GamePulseRead(FeedOutcome.Ok, new GamePulseAnswer(new Dictionary<long, GamePulseReading>()));
        }

        FeedOutcome outcome;
        int? httpCode = null;
        GamePulseAnswer? answer = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            var url = BaseUrl + string.Join(',', universeIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            using var response = await _http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token)
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
                var body = await FeedBody.ReadLimitedAsync(response, MaxBodyBytes, cts.Token).ConfigureAwait(false);
                answer = body is null ? null : Parse(body, universeIds, DateTimeOffset.UtcNow);
                outcome = answer is null ? FeedOutcome.Shape : FeedOutcome.Ok;
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

        _log.LogDebug("game-pulse read: {Outcome} (HTTP {Code}, {Count} ids)", outcome, httpCode?.ToString() ?? "none", universeIds.Count);
        return new GamePulseRead(outcome, outcome == FeedOutcome.Ok ? answer : null);
    }

    /// <summary>
    /// Null when the body has no <c>data</c> array. A game whose count is outside its range is left out;
    /// a date outside its range reads as no date. Either is dropped for that game alone.
    /// </summary>
    internal static GamePulseAnswer? Parse(byte[] body, IReadOnlyCollection<long> asked, DateTimeOffset now)
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

        if (envelope?.Data is not { } items)
        {
            return null;
        }

        var askedSet = new HashSet<long>(asked);
        var found = new Dictionary<long, GamePulseReading>();
        foreach (var item in items)
        {
            if (item is not { Id: long id, Playing: long playing } || !askedSet.Contains(id))
            {
                continue;
            }

            if (playing is < 0 or > MaxPlaying)
            {
                continue;
            }

            DateTimeOffset? updated = null;
            if (item.Updated is { } text
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                && parsed >= EarliestUpdated
                && parsed <= now.AddDays(1))
            {
                updated = parsed.ToUniversalTime();
            }

            found[id] = new GamePulseReading((int)playing, updated);
        }

        return new GamePulseAnswer(found);
    }
}
