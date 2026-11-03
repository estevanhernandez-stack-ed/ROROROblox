using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ROROROblox.Core;

/// <summary>
/// HttpClient-backed <see cref="IAccountFactsApi"/>. Typed client with exactly ONE constructor
/// (<c>HttpClient</c>, <c>ILogger&lt;AccountFactsApi&gt;</c>): a second applicable constructor throws at
/// resolve time, which a green unit suite and a clean build both miss
/// (<c>TypedHttpClientRegistrationTests</c> is the guard).
/// <para>
/// Cookie handling mirrors <see cref="RobloxApi"/>: the <c>.ROBLOSECURITY</c> value is attached per
/// request as a <c>Cookie</c> header, and the client behind it must be registered with
/// <c>UseCookies = false</c> or the first account's cookie lands in a shared container and every
/// later account fetches as that user. That line is duplicated at the registration on purpose
/// (the rule is easier to see twice than hidden in a shared factory) and a registration test reads
/// the primary handler for both clients.
/// </para>
/// <para>
/// Log lines carry the endpoint name and the status, never the cookie, never a URL (a URL holds a
/// user id) and never a response body.
/// </para>
/// </summary>
public sealed class AccountFactsApi : IAccountFactsApi
{
    /// <summary>Cap on the membership probe body kept for the measurement.</summary>
    public const int MaxProbeBodyChars = 512;

    private const string CurrencyEndpoint = "https://economy.roblox.com/v1/user/currency";
    private const string FriendsHost = "https://friends.roblox.com/v1/users";
    private const string MembershipHost = "https://premiumfeatures.roblox.com/v1/users";

    // A hung socket must not park the single facts worker for HttpClient's 100 s default.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly ILogger<AccountFactsApi> _log;

    public AccountFactsApi(HttpClient http, ILogger<AccountFactsApi> log)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        // The registration sets RORORO/<version>; this is the same belt RobloxApi wears, for a
        // client built outside the container.
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            var version = typeof(AccountFactsApi).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));
        }
    }

    public async Task<long?> GetRobuxAsync(string cookie, CancellationToken ct)
    {
        RequireCookie(cookie);
        using var response = await SendAsync(CurrencyEndpoint, cookie, ct).ConfigureAwait(false);
        Log("currency", response);
        ThrowOnFailure(response);

        var root = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return root is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty("robux", out var robux)
            && robux.ValueKind == JsonValueKind.Number
            && robux.TryGetInt64(out var value)
                ? value
                : null;
    }

    public async Task<int?> GetFriendsOnlineCountAsync(string cookie, long userId, CancellationToken ct)
    {
        RequireCookie(cookie);
        RequireUserId(userId);
        using var response = await SendAsync($"{FriendsHost}/{userId}/friends/online", cookie, ct).ConfigureAwait(false);
        Log("friends-online", response);
        ThrowOnFailure(response);

        var root = await ReadJsonAsync(response, ct).ConfigureAwait(false);
        return root is { ValueKind: JsonValueKind.Object } obj
            && obj.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Array
                ? data.GetArrayLength()
                : null;
    }

    public async Task<MembershipProbe> GetMembershipAsync(string cookie, long userId, CancellationToken ct)
    {
        RequireCookie(cookie);
        RequireUserId(userId);
        using var response = await SendAsync($"{MembershipHost}/{userId}/validate-membership", cookie, ct).ConfigureAwait(false);
        Log("membership", response);

        string? body = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);
            var text = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            body = string.IsNullOrWhiteSpace(text)
                ? null
                : text.Length > MaxProbeBodyChars ? text[..MaxProbeBodyChars] : text;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Body read timed out: the status is still a measurement.
        }
        return new MembershipProbe((int)response.StatusCode, body);
    }

    private async Task<HttpResponseMessage> SendAsync(string url, string cookie, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);
        return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
    }

    private static void ThrowOnFailure(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new AccountFactsRequestException((int)response.StatusCode);
        }
    }

    private static async Task<JsonElement?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);
            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void Log(string endpoint, HttpResponseMessage response)
        => _log.LogDebug("AccountFactsApi {Endpoint} -> {Status}", endpoint, (int)response.StatusCode);

    private static void RequireCookie(string cookie)
    {
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }
        // A control character would make the header write throw a FormatException that echoes the value.
        CookieValidation.ThrowIfControlCharacters(cookie, nameof(cookie));
    }

    private static void RequireUserId(long userId)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId), "userId must be positive.");
        }
    }
}
