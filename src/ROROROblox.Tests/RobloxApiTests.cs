using System.Net;
using System.Net.Http.Json;
using System.Text;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Stubbed-HTTP coverage of <see cref="RobloxApi"/>. No live Roblox calls; that's the manual
/// smoke checklist's job before each release tag. Includes the 415 regression guard for the
/// Content-Type contract caught at spike-time 2026-05-03.
/// </summary>
public class RobloxApiTests
{
    private const string TestCookie = "FAKE_COOKIE_FOR_TESTS_ONLY";

    private static (RobloxApi api, StubHttpHandler stub) CreateApi()
    {
        var stub = new StubHttpHandler();
        var client = new HttpClient(stub);
        return (new RobloxApi(client), stub);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, params (string Key, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status);
        foreach (var (key, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(key, value);
        }
        return response;
    }

    [Fact]
    public async Task GetAuthTicketAsync_HappyPath_ReturnsTicketAfterCsrfDance()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "csrf-abc-123")));
        stub.EnqueueResponse(Response(HttpStatusCode.OK, ("RBX-Authentication-Ticket", "TICKET-XYZ")));

        var ticket = await api.GetAuthTicketAsync(TestCookie);

        Assert.Equal("TICKET-XYZ", ticket.Ticket);
        Assert.True(ticket.CapturedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task GetAuthTicketAsync_BothPostsSendContentTypeApplicationJson()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "t")));
        stub.EnqueueResponse(Response(HttpStatusCode.OK, ("RBX-Authentication-Ticket", "T")));

        await api.GetAuthTicketAsync(TestCookie);

        foreach (var req in stub.Requests)
        {
            Assert.NotNull(req.Content);
            Assert.Equal("application/json", req.Content!.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task GetAuthTicketAsync_BothPostsSendCookieAndReferer()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "t")));
        stub.EnqueueResponse(Response(HttpStatusCode.OK, ("RBX-Authentication-Ticket", "T")));

        await api.GetAuthTicketAsync(TestCookie);

        foreach (var req in stub.Requests)
        {
            Assert.Contains(req.Headers, h => h.Key == "Cookie" && h.Value.Any(v => v.Contains(TestCookie)));
            Assert.Contains(req.Headers, h => h.Key == "Referer" && h.Value.Any(v => v == "https://www.roblox.com/"));
        }
    }

    [Fact]
    public async Task GetAuthTicketAsync_SecondPostHasCsrfToken_FirstDoesNot()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "csrf-token-value")));
        stub.EnqueueResponse(Response(HttpStatusCode.OK, ("RBX-Authentication-Ticket", "T")));

        await api.GetAuthTicketAsync(TestCookie);

        Assert.DoesNotContain(stub.Requests[0].Headers, h => h.Key == "X-CSRF-TOKEN");
        Assert.Contains(stub.Requests[1].Headers,
            h => h.Key == "X-CSRF-TOKEN" && h.Value.Any(v => v == "csrf-token-value"));
    }

    [Fact]
    public async Task GetAuthTicketAsync_401OnFirstPost_ThrowsCookieExpired()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<CookieExpiredException>(() => api.GetAuthTicketAsync(TestCookie));
    }

    [Fact]
    public async Task GetAuthTicketAsync_401OnSecondPost_ThrowsCookieExpired()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "t")));
        stub.EnqueueResponse(Response(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<CookieExpiredException>(() => api.GetAuthTicketAsync(TestCookie));
    }

    [Fact]
    public async Task GetAuthTicketAsync_415_ThrowsHelpfulError_RegressionGuardForSpikeFinding()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.UnsupportedMediaType));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetAuthTicketAsync(TestCookie));
        Assert.Contains("Content-Type", ex.Message);
        Assert.Contains("§5.7", ex.Message);
    }

    [Fact]
    public async Task GetAuthTicketAsync_NoCsrfTokenHeader_Throws()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.Forbidden));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetAuthTicketAsync(TestCookie));
        Assert.Contains("X-CSRF-TOKEN", ex.Message);
    }

    [Fact]
    public async Task GetAuthTicketAsync_NoTicketHeader_Throws()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("x-csrf-token", "t")));
        stub.EnqueueResponse(Response(HttpStatusCode.OK));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetAuthTicketAsync(TestCookie));
        Assert.Contains("RBX-Authentication-Ticket", ex.Message);
    }

    [Fact]
    public async Task GetAuthTicketAsync_TicketHeaderCasing_InsensitiveLookup()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.Forbidden, ("X-CSRF-Token", "t")));
        stub.EnqueueResponse(Response(HttpStatusCode.OK, ("rbx-authentication-ticket", "T-LOWER")));

        var ticket = await api.GetAuthTicketAsync(TestCookie);

        Assert.Equal("T-LOWER", ticket.Ticket);
    }

    [Fact]
    public async Task GetAuthTicketAsync_RejectsEmptyCookie()
    {
        var (api, _) = CreateApi();
        await Assert.ThrowsAsync<ArgumentException>(() => api.GetAuthTicketAsync(""));
    }

    [Fact]
    public async Task GetUserProfileAsync_HappyPath_ReturnsUserIdUsernameDisplayName()
    {
        var (api, stub) = CreateApi();
        var json = """{"id": 12345, "name": "TestUser", "displayName": "Test User Display"}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        stub.EnqueueResponse(response);

        var profile = await api.GetUserProfileAsync(TestCookie);

        Assert.Equal(12345, profile.UserId);
        Assert.Equal("TestUser", profile.Username);
        Assert.Equal("Test User Display", profile.DisplayName);
    }

    [Fact]
    public async Task GetUserProfileAsync_401_ThrowsCookieExpired()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<CookieExpiredException>(() => api.GetUserProfileAsync(TestCookie));
    }

    [Fact]
    public async Task GetAvatarHeadshotUrlAsync_HappyPath_ReturnsImageUrlFromFirstDataItem()
    {
        var (api, stub) = CreateApi();
        var json = """{"data": [{"imageUrl": "https://tr.rbxcdn.com/avatar/headshot.png"}]}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        stub.EnqueueResponse(response);

        var url = await api.GetAvatarHeadshotUrlAsync(userId: 12345);

        Assert.Equal("https://tr.rbxcdn.com/avatar/headshot.png", url);
    }

    [Fact]
    public async Task GetAvatarHeadshotUrlAsync_RejectsZeroOrNegativeUserId()
    {
        var (api, _) = CreateApi();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetAvatarHeadshotUrlAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetAvatarHeadshotUrlAsync(-1));
    }

    [Fact]
    public async Task GetAvatarHeadshotUrlAsync_EmptyData_Throws()
    {
        var (api, stub) = CreateApi();
        var json = """{"data": []}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        stub.EnqueueResponse(response);

        await Assert.ThrowsAsync<InvalidOperationException>(() => api.GetAvatarHeadshotUrlAsync(12345));
    }

    [Fact]
    public void Constructor_RejectsNullHttpClient()
    {
        Assert.Throws<ArgumentNullException>(() => new RobloxApi(null!));
    }

    // === GetGameMetadataByPlaceIdAsync ===

    [Fact]
    public async Task GetGameMetadataByPlaceIdAsync_HappyPath_ReturnsAllFields()
    {
        var (api, stub) = CreateApi();
        // Step 1: place -> universe
        stub.EnqueueResponse(JsonResponse("""{"universeId": 1234567}"""));
        // Step 2: universe -> game name
        stub.EnqueueResponse(JsonResponse("""{"data": [{"name": "Adopt Me!"}]}"""));
        // Step 3: universe -> icon
        stub.EnqueueResponse(JsonResponse("""{"data": [{"imageUrl": "https://tr.rbxcdn.com/icon.png"}]}"""));

        var result = await api.GetGameMetadataByPlaceIdAsync(920587237);

        Assert.NotNull(result);
        Assert.Equal(920587237, result!.PlaceId);
        Assert.Equal(1234567, result.UniverseId);
        Assert.Equal("Adopt Me!", result.Name);
        Assert.Equal("https://tr.rbxcdn.com/icon.png", result.IconUrl);
    }

    [Fact]
    public async Task GetGameMetadataByPlaceIdAsync_PlaceLookupFails_ReturnsNull()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(System.Net.HttpStatusCode.NotFound));

        var result = await api.GetGameMetadataByPlaceIdAsync(999);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetGameMetadataByPlaceIdAsync_IconFetchFails_StillReturnsMetadataWithEmptyIcon()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(JsonResponse("""{"universeId": 1234567}"""));
        stub.EnqueueResponse(JsonResponse("""{"data": [{"name": "Adopt Me!"}]}"""));
        stub.EnqueueResponse(Response(System.Net.HttpStatusCode.InternalServerError));

        var result = await api.GetGameMetadataByPlaceIdAsync(920587237);

        Assert.NotNull(result);
        Assert.Equal("Adopt Me!", result!.Name);
        Assert.Equal(string.Empty, result.IconUrl);
    }

    [Fact]
    public async Task GetGameMetadataByPlaceIdAsync_RejectsNonPositivePlaceId()
    {
        var (api, _) = CreateApi();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetGameMetadataByPlaceIdAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetGameMetadataByPlaceIdAsync(-1));
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    // === SearchGamesAsync ===

    [Fact]
    public async Task SearchGamesAsync_HappyPath_ReturnsGamesWithIcons()
    {
        var (api, stub) = CreateApi();
        // Step 1: omni-search returns mixed content groups; we keep only Game.
        stub.EnqueueResponse(JsonResponse("""
            {
              "searchResults": [
                {
                  "contentGroupType": "Game",
                  "contents": [
                    {"universeId": 100, "rootPlaceId": 920587237, "name": "Adopt Me!", "creatorName": "DreamCraft", "playerCount": 50000},
                    {"universeId": 200, "rootPlaceId": 142823291, "name": "Murder Mystery", "creatorName": "Nikilis", "playerCount": 30000}
                  ]
                },
                {
                  "contentGroupType": "User",
                  "contents": [
                    {"universeId": 999, "rootPlaceId": 0, "name": "ShouldBeFiltered", "creatorName": null, "playerCount": 0}
                  ]
                }
              ]
            }
            """));
        // Step 2: bulk icon fetch.
        stub.EnqueueResponse(JsonResponse("""
            {"data": [
              {"targetId": 100, "imageUrl": "https://tr.rbxcdn.com/100.png"},
              {"targetId": 200, "imageUrl": "https://tr.rbxcdn.com/200.png"}
            ]}
            """));

        var results = await api.SearchGamesAsync("adopt me");

        Assert.Equal(2, results.Count);
        Assert.Equal("Adopt Me!", results[0].Name);
        Assert.Equal(920587237, results[0].PlaceId);
        Assert.Equal("DreamCraft", results[0].CreatorName);
        Assert.Equal(50000, results[0].PlayerCount);
        Assert.Equal("https://tr.rbxcdn.com/100.png", results[0].IconUrl);
        Assert.Equal("Murder Mystery", results[1].Name);
    }

    [Fact]
    public async Task SearchGamesAsync_EmptyQuery_ReturnsEmpty()
    {
        var (api, stub) = CreateApi();

        var results = await api.SearchGamesAsync("   ");

        Assert.Empty(results);
        Assert.Empty(stub.Requests);  // didn't even hit the wire
    }

    [Fact]
    public async Task SearchGamesAsync_NetworkFailure_ReturnsEmpty()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(Response(System.Net.HttpStatusCode.InternalServerError));

        var results = await api.SearchGamesAsync("anything");

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchGamesAsync_IconFetchFails_StillReturnsResultsWithEmptyIcons()
    {
        var (api, stub) = CreateApi();
        stub.EnqueueResponse(JsonResponse("""
            {"searchResults": [
              {"contentGroupType": "Game", "contents": [
                {"universeId": 100, "rootPlaceId": 1, "name": "X", "creatorName": "Y", "playerCount": 5}
              ]}
            ]}
            """));
        stub.EnqueueResponse(Response(System.Net.HttpStatusCode.NotFound));

        var results = await api.SearchGamesAsync("x");

        Assert.Single(results);
        Assert.Equal("X", results[0].Name);
        Assert.Equal(string.Empty, results[0].IconUrl);
    }
}
