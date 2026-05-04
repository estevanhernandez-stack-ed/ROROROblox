using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ROROROblox.Core;

/// <summary>
/// HttpClient-backed implementation of <see cref="IRobloxApi"/>. Single <see cref="HttpClient"/>
/// instance via <c>IHttpClientFactory</c> at composition root (item 8). Tests inject a custom
/// <see cref="HttpMessageHandler"/> via the public constructor.
/// </summary>
public sealed class RobloxApi : IRobloxApi
{
    private const string AuthTicketEndpoint = "https://auth.roblox.com/v1/authentication-ticket";
    private const string AuthenticatedUserEndpoint = "https://users.roblox.com/v1/users/authenticated";
    private const string AvatarHeadshotEndpoint = "https://thumbnails.roblox.com/v1/users/avatar-headshot";
    private const string Referer = "https://www.roblox.com/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _httpClient;

    public RobloxApi(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        var version = typeof(RobloxApi).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ROROROblox", version));
        }
    }

    public async Task<AuthTicket> GetAuthTicketAsync(string cookie)
    {
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }

        // First POST — discover the X-CSRF-TOKEN.
        using var firstResponse = await PostAuthTicketAsync(cookie, csrfToken: null).ConfigureAwait(false);
        ThrowOnAuthFailure(firstResponse);
        ThrowOnContentTypeRejection(firstResponse);

        if (!firstResponse.Headers.TryGetValues("x-csrf-token", out var csrfTokens))
        {
            throw new InvalidOperationException(
                $"Roblox auth-ticket endpoint did not return X-CSRF-TOKEN. Status={(int)firstResponse.StatusCode}.");
        }

        var csrfToken = csrfTokens.FirstOrDefault()
            ?? throw new InvalidOperationException("X-CSRF-TOKEN header was empty.");

        // Second POST — exchange cookie + token for ticket.
        using var secondResponse = await PostAuthTicketAsync(cookie, csrfToken).ConfigureAwait(false);
        ThrowOnAuthFailure(secondResponse);
        ThrowOnContentTypeRejection(secondResponse);

        if (!secondResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Roblox auth-ticket endpoint returned {(int)secondResponse.StatusCode}.");
        }

        if (!secondResponse.Headers.TryGetValues("RBX-Authentication-Ticket", out var ticketHeaders))
        {
            throw new InvalidOperationException("Auth ticket response missing RBX-Authentication-Ticket header.");
        }

        var ticket = ticketHeaders.FirstOrDefault();
        if (string.IsNullOrEmpty(ticket))
        {
            throw new InvalidOperationException("RBX-Authentication-Ticket header was empty.");
        }

        return new AuthTicket(ticket, DateTimeOffset.UtcNow);
    }

    public async Task<UserProfile> GetUserProfileAsync(string cookie)
    {
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, AuthenticatedUserEndpoint);
        request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        ThrowOnAuthFailure(response);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Roblox user profile endpoint returned {(int)response.StatusCode}.");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<AuthenticatedUserResponse>(JsonOptions)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Authenticated-user response was empty.");

        return new UserProfile(payload.Id, payload.Name, payload.DisplayName);
    }

    public async Task<string> GetAvatarHeadshotUrlAsync(long userId)
    {
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId), "userId must be positive.");
        }

        var url = $"{AvatarHeadshotEndpoint}?userIds={userId}&size=150x150&format=Png&isCircular=false";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Roblox avatar endpoint returned {(int)response.StatusCode}.");
        }

        var payload = await response.Content
            .ReadFromJsonAsync<ThumbnailsResponse>(JsonOptions)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Avatar response was empty.");

        var first = payload.Data?.FirstOrDefault()
            ?? throw new InvalidOperationException("Avatar response had no data items.");

        if (string.IsNullOrEmpty(first.ImageUrl))
        {
            throw new InvalidOperationException("Avatar response had an empty imageUrl.");
        }

        return first.ImageUrl;
    }

    private async Task<HttpResponseMessage> PostAuthTicketAsync(string cookie, string? csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AuthTicketEndpoint);
        request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
        request.Headers.Add("Referer", Referer);
        if (!string.IsNullOrEmpty(csrfToken))
        {
            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        }
        // Roblox returns 415 without explicit Content-Type even on empty bodies (caught at spike-time, see spec §5.7).
        request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");
        return await _httpClient.SendAsync(request).ConfigureAwait(false);
    }

    private static void ThrowOnAuthFailure(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new CookieExpiredException();
        }
    }

    private static void ThrowOnContentTypeRejection(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.UnsupportedMediaType)
        {
            throw new InvalidOperationException(
                "auth-ticket endpoint rejected Content-Type — re-check spec §5.7. " +
                "Roblox requires Content-Type: application/json on these POSTs.");
        }
    }

    // Wire-shape records for JSON deserialization. Internal — never surface these to callers.
    private sealed record AuthenticatedUserResponse(long Id, string Name, string DisplayName);
    private sealed record ThumbnailsResponse(ThumbnailItem[] Data);
    private sealed record ThumbnailItem([property: JsonPropertyName("imageUrl")] string ImageUrl);
}
