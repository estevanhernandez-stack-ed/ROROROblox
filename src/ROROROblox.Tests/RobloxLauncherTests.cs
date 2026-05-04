using System.ComponentModel;
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class RobloxLauncherTests
{
    private const string TestCookie = "FAKE_COOKIE_FOR_TESTS_ONLY";
    private const string TestPlaceUrl = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?placeId=920587237";

    // === BuildLaunchUri (pure / snapshot) ===

    [Fact]
    public void BuildLaunchUri_HasExpectedShape_WithFixedInputs()
    {
        var uri = RobloxLauncher.BuildLaunchUri(
            ticket: "TICKET-AAA",
            launchTime: 1714780000000,
            browserTrackerId: "1234567890123",
            placeUrl: "https://example.com/place?placeId=42");

        var expected =
            "roblox-player:1+launchmode:play" +
            "+gameinfo:TICKET-AAA" +
            "+launchtime:1714780000000" +
            "+placelauncherurl:https%3A%2F%2Fexample.com%2Fplace%3FplaceId%3D42" +
            "+browsertrackerid:1234567890123" +
            "+robloxLocale:en_us+gameLocale:en_us";

        Assert.Equal(expected, uri);
    }

    [Fact]
    public void BuildLaunchUri_EncodesPlaceUrl()
    {
        var uri = RobloxLauncher.BuildLaunchUri(
            ticket: "T",
            launchTime: 0,
            browserTrackerId: "1",
            placeUrl: "https://x.example/path with spaces&query=1");

        Assert.Contains(
            "+placelauncherurl:https%3A%2F%2Fx.example%2Fpath%20with%20spaces%26query%3D1",
            uri);
    }

    [Fact]
    public void BuildLaunchUri_RejectsEmptyTicketPlaceUrlOrTrackerId()
    {
        Assert.Throws<ArgumentException>(() =>
            RobloxLauncher.BuildLaunchUri("", 0, "1", "https://x"));
        Assert.Throws<ArgumentException>(() =>
            RobloxLauncher.BuildLaunchUri("T", 0, "1", ""));
        Assert.Throws<ArgumentException>(() =>
            RobloxLauncher.BuildLaunchUri("T", 0, "", "https://x"));
    }

    // === NormalizeToPlaceLauncherUrl ===

    [Fact]
    public void NormalizeToPlaceLauncherUrl_PublicGameUrl_RewritesToPlaceLauncherForm()
    {
        var result = RobloxLauncher.NormalizeToPlaceLauncherUrl(
            "https://www.roblox.com/games/920587237/Adopt-Me",
            browserTrackerId: "12345");

        Assert.Contains("assetgame.roblox.com/game/PlaceLauncher.ashx", result);
        Assert.Contains("placeId=920587237", result);
        Assert.Contains("browserTrackerId=12345", result);
        Assert.Contains("request=RequestGame", result);
    }

    [Fact]
    public void NormalizeToPlaceLauncherUrl_PublicGameUrl_WithoutSlug_StillExtractsId()
    {
        var result = RobloxLauncher.NormalizeToPlaceLauncherUrl(
            "https://www.roblox.com/games/920587237",
            browserTrackerId: "12345");

        Assert.Contains("placeId=920587237", result);
    }

    [Fact]
    public void NormalizeToPlaceLauncherUrl_AlreadyPlaceLauncherUrl_PassesThroughUnchanged()
    {
        var input = "https://assetgame.roblox.com/game/PlaceLauncher.ashx?request=RequestGame&browserTrackerId=99&placeId=12345&isPlayTogetherGame=false";

        var result = RobloxLauncher.NormalizeToPlaceLauncherUrl(input, browserTrackerId: "12345");

        Assert.Equal(input, result);
    }

    [Fact]
    public void NormalizeToPlaceLauncherUrl_BareNumericPlaceId_WrapsInPlaceLauncherForm()
    {
        var result = RobloxLauncher.NormalizeToPlaceLauncherUrl(
            "920587237",
            browserTrackerId: "12345");

        Assert.Contains("placeId=920587237", result);
        Assert.Contains("PlaceLauncher.ashx", result);
    }

    [Fact]
    public void NormalizeToPlaceLauncherUrl_UnrecognizedInput_PassesThrough()
    {
        var input = "https://example.com/some/random/url";
        var result = RobloxLauncher.NormalizeToPlaceLauncherUrl(input, browserTrackerId: "12345");
        Assert.Equal(input, result);
    }

    [Fact]
    public void NormalizeToPlaceLauncherUrl_WorksWithoutWww()
    {
        var result = RobloxLauncher.NormalizeToPlaceLauncherUrl(
            "https://roblox.com/games/920587237/Adopt-Me",
            browserTrackerId: "1");

        Assert.Contains("placeId=920587237", result);
    }

    [Fact]
    public async Task LaunchAsync_TransformsPublicUrlBeforeBuildingLaunchUri()
    {
        var (launcher, _, processStarter) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: null,
            startResult: 1);

        await launcher.LaunchAsync(TestCookie, placeUrl: "https://www.roblox.com/games/920587237/Adopt-Me");

        // The roblox-player URI's placelauncherurl segment should contain the URL-encoded
        // PlaceLauncher.ashx form, not the public web URL.
        Assert.Contains(Uri.EscapeDataString("PlaceLauncher.ashx"), processStarter.LastUri);
        Assert.Contains(Uri.EscapeDataString("placeId=920587237"), processStarter.LastUri);
        Assert.DoesNotContain(Uri.EscapeDataString("/games/920587237/Adopt-Me"), processStarter.LastUri);
    }

    // === LaunchAsync ===

    [Fact]
    public async Task LaunchAsync_HappyPath_ReturnsStartedWithPid()
    {
        var (launcher, _, processStarter) = CreateLauncher(
            ticket: "TICKET-1",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 12345);

        var result = await launcher.LaunchAsync(TestCookie);

        var started = Assert.IsType<LaunchResult.Started>(result);
        Assert.Equal(12345, started.Pid);
        Assert.NotNull(processStarter.LastUri);
        Assert.Contains("roblox-player:1", processStarter.LastUri);
        Assert.Contains("+gameinfo:TICKET-1", processStarter.LastUri);
        Assert.Contains("+placelauncherurl:", processStarter.LastUri);
    }

    [Fact]
    public async Task LaunchAsync_UsesExplicitPlaceUrl_OverSettingsDefault()
    {
        var (launcher, _, processStarter) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: "https://settings-default",
            startResult: 1);

        await launcher.LaunchAsync(TestCookie, placeUrl: "https://explicit-place");

        Assert.Contains(Uri.EscapeDataString("https://explicit-place"), processStarter.LastUri);
        Assert.DoesNotContain(Uri.EscapeDataString("https://settings-default"), processStarter.LastUri);
    }

    [Fact]
    public async Task LaunchAsync_NullPlaceUrl_FallsBackToSettingsDefault()
    {
        var (launcher, _, processStarter) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1);

        await launcher.LaunchAsync(TestCookie, placeUrl: null);

        Assert.Contains(Uri.EscapeDataString(TestPlaceUrl), processStarter.LastUri);
    }

    [Fact]
    public async Task LaunchAsync_NoPlaceUrl_AndNoDefault_ReturnsFailed()
    {
        var (launcher, _, _) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: null,
            startResult: 1);

        var result = await launcher.LaunchAsync(TestCookie, placeUrl: null);

        var failed = Assert.IsType<LaunchResult.Failed>(result);
        Assert.Contains("default", failed.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LaunchAsync_CookieExpired_ReturnsCookieExpiredResult()
    {
        var api = new StubRobloxApi(_ => throw new CookieExpiredException());
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ => 1);
        var launcher = new RobloxLauncher(api, settings, processStarter);

        var result = await launcher.LaunchAsync(TestCookie);

        Assert.IsType<LaunchResult.CookieExpired>(result);
    }

    [Fact]
    public async Task LaunchAsync_Win32Exception_ReturnsFailedWithRobloxNotInstalledMessage()
    {
        var (launcher, _, _) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startThrows: new Win32Exception("No application is associated with the specified file."));

        var result = await launcher.LaunchAsync(TestCookie);

        var failed = Assert.IsType<LaunchResult.Failed>(result);
        Assert.Contains("Roblox does not appear to be installed", failed.Message);
    }

    [Fact]
    public async Task LaunchAsync_RejectsEmptyCookie()
    {
        var (launcher, _, _) = CreateLauncher("T", TestPlaceUrl, startResult: 1);

        await Assert.ThrowsAsync<ArgumentException>(() => launcher.LaunchAsync(""));
    }

    [Fact]
    public async Task LaunchAsync_UriIncludesAllRequiredSegments()
    {
        var (launcher, _, processStarter) = CreateLauncher(
            ticket: "T-EXPECT",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1);

        await launcher.LaunchAsync(TestCookie);

        Assert.StartsWith("roblox-player:1+launchmode:play", processStarter.LastUri);
        Assert.Contains("+gameinfo:T-EXPECT", processStarter.LastUri);
        Assert.Contains("+launchtime:", processStarter.LastUri);
        Assert.Contains("+placelauncherurl:", processStarter.LastUri);
        Assert.Contains("+browsertrackerid:", processStarter.LastUri);
        Assert.EndsWith("+robloxLocale:en_us+gameLocale:en_us", processStarter.LastUri);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        var api = new StubRobloxApi(_ => Task.FromResult(new AuthTicket("T", DateTimeOffset.UtcNow)));
        var settings = new InMemoryAppSettings();
        var ps = new RecordingProcessStarter(_ => 1);

        Assert.Throws<ArgumentNullException>(() => new RobloxLauncher(null!, settings, ps));
        Assert.Throws<ArgumentNullException>(() => new RobloxLauncher(api, null!, ps));
        Assert.Throws<ArgumentNullException>(() => new RobloxLauncher(api, settings, null!));
    }

    // === Helpers ===

    private static (RobloxLauncher, InMemoryAppSettings, RecordingProcessStarter) CreateLauncher(
        string ticket,
        string? defaultPlaceUrl,
        int startResult = 1,
        Exception? startThrows = null)
    {
        var api = new StubRobloxApi(_ => Task.FromResult(new AuthTicket(ticket, DateTimeOffset.UtcNow)));
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = defaultPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ =>
        {
            if (startThrows is not null) throw startThrows;
            return startResult;
        });
        var launcher = new RobloxLauncher(api, settings, processStarter);
        return (launcher, settings, processStarter);
    }

    private sealed class StubRobloxApi : IRobloxApi
    {
        private readonly Func<string, Task<AuthTicket>> _ticketBehavior;

        public StubRobloxApi(Func<string, Task<AuthTicket>> ticketBehavior)
        {
            _ticketBehavior = ticketBehavior;
        }

        public Task<AuthTicket> GetAuthTicketAsync(string cookie) => _ticketBehavior(cookie);
        public Task<UserProfile> GetUserProfileAsync(string cookie) => throw new NotImplementedException();
        public Task<string> GetAvatarHeadshotUrlAsync(long userId) => throw new NotImplementedException();
        public Task<GameMetadata?> GetGameMetadataByPlaceIdAsync(long placeId) => throw new NotImplementedException();
    }

    private sealed class InMemoryAppSettings : IAppSettings
    {
        public string? DefaultPlaceUrl { get; set; }

        public Task<string?> GetDefaultPlaceUrlAsync() => Task.FromResult(DefaultPlaceUrl);
        public Task SetDefaultPlaceUrlAsync(string url) { DefaultPlaceUrl = url; return Task.CompletedTask; }
    }

    private sealed class RecordingProcessStarter : IProcessStarter
    {
        private readonly Func<string, int> _behavior;
        public string LastUri { get; private set; } = string.Empty;

        public RecordingProcessStarter(Func<string, int> behavior)
        {
            _behavior = behavior;
        }

        public int StartViaShell(string fileNameOrUri)
        {
            LastUri = fileNameOrUri;
            return _behavior(fileNameOrUri);
        }
    }
}
