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
    private const string PlaceUniverseEndpoint = "https://apis.roblox.com/universes/v1/places";
    private const string GamesEndpoint = "https://games.roblox.com/v1/games";
    private const string GameIconsEndpoint = "https://thumbnails.roblox.com/v1/games/icons";
    private const string OmniSearchEndpoint = "https://apis.roblox.com/search-api/omni-search";
    private const string FriendsEndpoint = "https://friends.roblox.com/v1/users";
    private const string PresenceEndpoint = "https://presence.roblox.com/v1/presence/users";
    private const string ShareLinksEndpoint = "https://apis.roblox.com/sharelinks/v1/resolve-link";
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

    public async Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId)
    {
        if (placeId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(placeId), "placeId must be positive.");
        }

        // Step 1: place id -> universe id (public, no cookie).
        long universeId;
        try
        {
            using var universeRequest = new HttpRequestMessage(HttpMethod.Get, $"{PlaceUniverseEndpoint}/{placeId}/universe");
            using var universeResponse = await _httpClient.SendAsync(universeRequest).ConfigureAwait(false);
            if (!universeResponse.IsSuccessStatusCode)
            {
                return null;
            }
            var universePayload = await universeResponse.Content
                .ReadFromJsonAsync<UniverseLookupResponse>(JsonOptions)
                .ConfigureAwait(false);
            if (universePayload?.UniverseId is null or 0)
            {
                return null;
            }
            universeId = universePayload.UniverseId.Value;
        }
        catch
        {
            return null;
        }

        // Step 2: universe id -> game name (public, no cookie).
        string name;
        try
        {
            using var gamesRequest = new HttpRequestMessage(HttpMethod.Get, $"{GamesEndpoint}?universeIds={universeId}");
            using var gamesResponse = await _httpClient.SendAsync(gamesRequest).ConfigureAwait(false);
            if (!gamesResponse.IsSuccessStatusCode)
            {
                return null;
            }
            var gamesPayload = await gamesResponse.Content
                .ReadFromJsonAsync<GamesListResponse>(JsonOptions)
                .ConfigureAwait(false);
            var firstGame = gamesPayload?.Data?.FirstOrDefault();
            if (firstGame is null || string.IsNullOrEmpty(firstGame.Name))
            {
                return null;
            }
            name = firstGame.Name;
        }
        catch
        {
            return null;
        }

        // Step 3: universe id -> icon URL (public, no cookie). Soft-failure: if icon fetch fails,
        // still return metadata with empty IconUrl -- the favorite is still useful.
        var iconUrl = string.Empty;
        try
        {
            var iconQuery = $"?universeIds={universeId}&size=150x150&format=Png&isCircular=false";
            using var iconRequest = new HttpRequestMessage(HttpMethod.Get, $"{GameIconsEndpoint}{iconQuery}");
            using var iconResponse = await _httpClient.SendAsync(iconRequest).ConfigureAwait(false);
            if (iconResponse.IsSuccessStatusCode)
            {
                var iconPayload = await iconResponse.Content
                    .ReadFromJsonAsync<ThumbnailsResponse>(JsonOptions)
                    .ConfigureAwait(false);
                iconUrl = iconPayload?.Data?.FirstOrDefault()?.ImageUrl ?? string.Empty;
            }
        }
        catch
        {
        }

        return new GameMetadata(placeId, universeId, name, iconUrl);
    }

    public async Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var sessionId = Guid.NewGuid().ToString();
        var searchUrl = $"{OmniSearchEndpoint}?searchQuery={Uri.EscapeDataString(query)}&pageType=all&sessionId={sessionId}";

        OmniSearchResponse? payload;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }
            payload = await response.Content
                .ReadFromJsonAsync<OmniSearchResponse>(JsonOptions)
                .ConfigureAwait(false);
        }
        catch
        {
            return [];
        }

        var games = payload?.SearchResults?
            .Where(g => string.Equals(g.ContentGroupType, "Game", StringComparison.OrdinalIgnoreCase))
            .SelectMany(g => g.Contents ?? [])
            .Where(c => c.UniverseId > 0 && c.RootPlaceId > 0 && !string.IsNullOrEmpty(c.Name))
            .Take(20)
            .ToList()
            ?? [];

        if (games.Count == 0)
        {
            return [];
        }

        // Bulk icon fetch -- one HTTP call instead of N.
        var iconsByUniverseId = new Dictionary<long, string>();
        try
        {
            var universeIds = string.Join(",", games.Select(g => g.UniverseId));
            var iconUrl = $"{GameIconsEndpoint}?universeIds={universeIds}&size=150x150&format=Png&isCircular=false";
            using var iconRequest = new HttpRequestMessage(HttpMethod.Get, iconUrl);
            using var iconResponse = await _httpClient.SendAsync(iconRequest).ConfigureAwait(false);
            if (iconResponse.IsSuccessStatusCode)
            {
                var iconPayload = await iconResponse.Content
                    .ReadFromJsonAsync<GameIconsResponse>(JsonOptions)
                    .ConfigureAwait(false);
                if (iconPayload?.Data != null)
                {
                    foreach (var item in iconPayload.Data)
                    {
                        if (item.TargetId > 0 && !string.IsNullOrEmpty(item.ImageUrl))
                        {
                            iconsByUniverseId[item.TargetId] = item.ImageUrl;
                        }
                    }
                }
            }
        }
        catch
        {
            // Best-effort; results without icons still useful.
        }

        return games.Select(g => new GameSearchResult(
            PlaceId: g.RootPlaceId,
            UniverseId: g.UniverseId,
            Name: g.Name,
            CreatorName: g.CreatorName ?? string.Empty,
            PlayerCount: g.PlayerCount,
            IconUrl: iconsByUniverseId.TryGetValue(g.UniverseId, out var icon) ? icon : string.Empty
        )).ToList();
    }

    public async Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId)
    {
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }
        if (userId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(userId), "userId must be positive.");
        }

        FriendsListResponse? payload;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{FriendsEndpoint}/{userId}/friends");
            request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            ThrowOnAuthFailure(response);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }
            payload = await response.Content
                .ReadFromJsonAsync<FriendsListResponse>(JsonOptions)
                .ConfigureAwait(false);
        }
        catch (CookieExpiredException)
        {
            throw;
        }
        catch
        {
            return [];
        }

        var friends = payload?.Data?.Where(f => f.Id > 0).ToList() ?? [];
        if (friends.Count == 0)
        {
            return [];
        }

        // Bulk avatar fetch — one call instead of N. Best-effort; missing avatars stay empty.
        var avatarsByUserId = await BulkFetchAvatarsAsync(friends.Select(f => f.Id)).ConfigureAwait(false);

        return friends.Select(f => new Friend(
            UserId: f.Id,
            Username: f.Name ?? string.Empty,
            DisplayName: string.IsNullOrEmpty(f.DisplayName) ? (f.Name ?? string.Empty) : f.DisplayName,
            AvatarUrl: avatarsByUserId.TryGetValue(f.Id, out var url) ? url : string.Empty
        )).ToList();
    }

    public async Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds)
    {
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }
        ArgumentNullException.ThrowIfNull(userIds);

        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        PresenceResponse? payload;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, PresenceEndpoint);
            request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
            request.Content = JsonContent.Create(new PresenceRequest(ids), options: JsonOptions);
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            ThrowOnAuthFailure(response);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }
            payload = await response.Content
                .ReadFromJsonAsync<PresenceResponse>(JsonOptions)
                .ConfigureAwait(false);
        }
        catch (CookieExpiredException)
        {
            throw;
        }
        catch
        {
            return [];
        }

        return (payload?.UserPresences ?? [])
            .Where(p => p.UserId > 0)
            .Select(p => new UserPresence(
                UserId: p.UserId,
                PresenceType: MapPresenceType(p.UserPresenceType),
                PlaceId: p.PlaceId is > 0 ? p.PlaceId : null,
                GameJobId: string.IsNullOrEmpty(p.GameId) ? null : p.GameId,
                LastLocation: string.IsNullOrEmpty(p.LastLocation) ? null : p.LastLocation))
            .ToList();
    }

    private static UserPresenceType MapPresenceType(int raw) => raw switch
    {
        0 => UserPresenceType.Offline,
        1 => UserPresenceType.OnlineWebsite,
        2 => UserPresenceType.InGame,
        3 => UserPresenceType.InStudio,
        4 => UserPresenceType.Invisible,
        _ => UserPresenceType.Offline,
    };

    /// <summary>
    /// Bulk fetch avatar headshot URLs for many user ids in one call.
    /// Returns an empty dictionary on any failure — callers treat avatars as best-effort.
    /// </summary>
    private async Task<Dictionary<long, string>> BulkFetchAvatarsAsync(IEnumerable<long> userIds)
    {
        var ids = userIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        try
        {
            var query = $"?userIds={string.Join(",", ids)}&size=150x150&format=Png&isCircular=false";
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{AvatarHeadshotEndpoint}{query}");
            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }
            var payload = await response.Content
                .ReadFromJsonAsync<BulkThumbnailsResponse>(JsonOptions)
                .ConfigureAwait(false);
            var dict = new Dictionary<long, string>();
            if (payload?.Data is not null)
            {
                foreach (var item in payload.Data)
                {
                    if (item.TargetId > 0 && !string.IsNullOrEmpty(item.ImageUrl))
                    {
                        dict[item.TargetId] = item.ImageUrl;
                    }
                }
            }
            return dict;
        }
        catch
        {
            return [];
        }
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

    public async Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType)
    {
        if (string.IsNullOrEmpty(cookie))
        {
            throw new ArgumentException("Cookie must not be empty.", nameof(cookie));
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(linkType))
        {
            linkType = "Server";
        }

        // Same CSRF-dance shape as the auth-ticket endpoint: first call surfaces the token,
        // second call sends it. Body shape: { linkId, linkType }.
        var bodyJson = JsonSerializer.Serialize(new ResolveShareLinkRequest(code, linkType), JsonOptions);

        using var firstResponse = await PostShareLinkAsync(cookie, bodyJson, csrfToken: null).ConfigureAwait(false);
        ThrowOnAuthFailure(firstResponse);

        string csrfToken;
        if (firstResponse.IsSuccessStatusCode)
        {
            // Some Roblox tenants return 200 directly without a CSRF challenge — accept either.
            return await ParseShareLinkResponseAsync(firstResponse, linkType).ConfigureAwait(false);
        }
        else if (firstResponse.StatusCode == HttpStatusCode.Forbidden
            && firstResponse.Headers.TryGetValues("x-csrf-token", out var csrfTokens))
        {
            csrfToken = csrfTokens.FirstOrDefault() ?? string.Empty;
            if (string.IsNullOrEmpty(csrfToken))
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        using var secondResponse = await PostShareLinkAsync(cookie, bodyJson, csrfToken).ConfigureAwait(false);
        ThrowOnAuthFailure(secondResponse);
        if (!secondResponse.IsSuccessStatusCode)
        {
            return null;
        }

        return await ParseShareLinkResponseAsync(secondResponse, linkType).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> PostShareLinkAsync(string cookie, string bodyJson, string? csrfToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ShareLinksEndpoint);
        request.Headers.Add("Cookie", $".ROBLOSECURITY={cookie}");
        request.Headers.Add("Referer", Referer);
        if (!string.IsNullOrEmpty(csrfToken))
        {
            request.Headers.Add("X-CSRF-TOKEN", csrfToken);
        }
        request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        return await _httpClient.SendAsync(request).ConfigureAwait(false);
    }

    private static async Task<ShareLinkResolution?> ParseShareLinkResponseAsync(HttpResponseMessage response, string requestedLinkType)
    {
        ShareLinkResponse? payload;
        try
        {
            payload = await response.Content
                .ReadFromJsonAsync<ShareLinkResponse>(JsonOptions)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }

        if (payload is null)
        {
            return null;
        }

        // Server kind: privateServerInviteData carries placeId + linkCode. Status must be Valid.
        var invite = payload.PrivateServerInviteData;
        if (invite is not null
            && string.Equals(invite.Status, "Valid", StringComparison.OrdinalIgnoreCase)
            && invite.PlaceId > 0
            && !string.IsNullOrEmpty(invite.LinkCode))
        {
            return new ShareLinkResolution(
                LinkType: payload.LinkType ?? requestedLinkType,
                PlaceId: invite.PlaceId,
                LinkCode: invite.LinkCode);
        }

        // Other types (Game / Profile / etc.) aren't load-bearing for v1 — caller branches on
        // empty PlaceId. Surface the LinkType so the caller knows what came back.
        return new ShareLinkResolution(
            LinkType: payload.LinkType ?? requestedLinkType,
            PlaceId: 0,
            LinkCode: string.Empty);
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
    private sealed record UniverseLookupResponse(long? UniverseId);
    private sealed record GamesListResponse(GamesListItem[] Data);
    private sealed record GamesListItem(string Name);
    private sealed record OmniSearchResponse(OmniSearchGroup[]? SearchResults);
    private sealed record OmniSearchGroup(string ContentGroupType, OmniSearchContent[]? Contents);
    private sealed record OmniSearchContent(
        long UniverseId,
        long RootPlaceId,
        string Name,
        string? CreatorName,
        long PlayerCount);
    private sealed record GameIconsResponse(GameIconItem[] Data);
    private sealed record GameIconItem(
        long TargetId,
        [property: JsonPropertyName("imageUrl")] string ImageUrl);
    private sealed record FriendsListResponse(FriendItem[]? Data);
    private sealed record FriendItem(long Id, string? Name, string? DisplayName);
    private sealed record BulkThumbnailsResponse(BulkThumbnailItem[] Data);
    private sealed record BulkThumbnailItem(
        long TargetId,
        [property: JsonPropertyName("imageUrl")] string ImageUrl);
    private sealed record PresenceRequest([property: JsonPropertyName("userIds")] IReadOnlyList<long> UserIds);
    private sealed record PresenceResponse(PresenceItem[]? UserPresences);
    private sealed record PresenceItem(
        long UserId,
        int UserPresenceType,
        long? PlaceId,
        string? GameId,
        string? LastLocation);
    private sealed record ResolveShareLinkRequest(
        [property: JsonPropertyName("linkId")] string LinkId,
        [property: JsonPropertyName("linkType")] string LinkType);
    private sealed record ShareLinkResponse(
        string? LinkType,
        ShareLinkInviteData? PrivateServerInviteData);
    private sealed record ShareLinkInviteData(
        long PlaceId,
        string? LinkCode,
        string? Status);
}
