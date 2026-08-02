using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ROROROblox.Core;
using ROROROblox.Core.Diagnostics;

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
    public async Task LaunchAsync_SessionLimited_ReturnsLimitedResult()
    {
        var api = new StubRobloxApi(_ => throw new SessionLimitedException());
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ => 1);
        var launcher = new RobloxLauncher(api, settings, processStarter);

        var result = await launcher.LaunchAsync(TestCookie);

        Assert.IsType<LaunchResult.Limited>(result);
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
    public async Task LaunchAsync_StableBrowserTrackerId_WinsOverRandomFactory()
    {
        // v1.8.1 trust hygiene: a caller-supplied stable btid must reach the launch URI
        // verbatim on BOTH overloads — the random per-launch factory is the fallback only.
        var (launcher, _, processStarter) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1);

        await launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(920587237), browserTrackerId: 7777777777777);
        // The btid rides TWICE in a launch URI: the outer +browsertrackerid: segment AND the
        // browserTrackerId= query param embedded in the (escaped) placelauncherurl. Assert both
        // so a regression that reverts one source to the random factory — leaving a single URI
        // carrying two different tracker ids — fails the test.
        Assert.Contains("+browsertrackerid:7777777777777", processStarter.LastUri);
        Assert.Contains("browserTrackerId%3D7777777777777", processStarter.LastUri);

        await launcher.LaunchAsync(TestCookie, placeUrl: TestPlaceUrl, browserTrackerId: 8888888888888);
        Assert.Contains("+browsertrackerid:8888888888888", processStarter.LastUri);
    }

    [Fact]
    public async Task LaunchAsync_NullBrowserTrackerId_FallsBackToFactory()
    {
        var (launcher, _, processStarter) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1);

        await launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(920587237));

        // The public ctor's factory produces a 13-digit value — presence is enough here;
        // the exact-value path is covered by the stable-btid test above.
        Assert.Matches(@"\+browsertrackerid:\d{13}\+", processStarter.LastUri);
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

    // === LaunchAsync(LaunchTarget) — typed API ===

    [Fact]
    public async Task LaunchAsync_TypedApi_PrivateServer_BuildsRequestPrivateGameUri()
    {
        var (launcher, _, processStarter) = CreateLauncher(
            ticket: "TKT",
            defaultPlaceUrl: null,
            startResult: 1);

        var result = await launcher.LaunchAsync(
            TestCookie,
            new LaunchTarget.PrivateServer(920587237, "share-code-xyz", PrivateServerCodeKind.LinkCode));

        Assert.IsType<LaunchResult.Started>(result);
        // Encoded inside placelauncherurl segment. LinkCode kind => emits linkCode=, never accessCode=.
        Assert.Contains(Uri.EscapeDataString("request=RequestPrivateGame"), processStarter.LastUri);
        Assert.Contains(Uri.EscapeDataString("placeId=920587237"), processStarter.LastUri);
        Assert.Contains(Uri.EscapeDataString("linkCode=share-code-xyz"), processStarter.LastUri);
        Assert.DoesNotContain(Uri.EscapeDataString("accessCode="), processStarter.LastUri);
    }

    [Fact]
    public async Task LaunchAsync_TypedApi_FollowFriend_BuildsRequestFollowUserUri()
    {
        var (launcher, _, processStarter) = CreateLauncher(
            ticket: "TKT",
            defaultPlaceUrl: null,
            startResult: 1);

        var result = await launcher.LaunchAsync(TestCookie, new LaunchTarget.FollowFriend(98765));

        Assert.IsType<LaunchResult.Started>(result);
        Assert.Contains(Uri.EscapeDataString("request=RequestFollowUser"), processStarter.LastUri);
        Assert.Contains(Uri.EscapeDataString("userId=98765"), processStarter.LastUri);
    }

    [Fact]
    public async Task LaunchAsync_TypedApi_NoFavoriteDefault_IgnoresLegacySettingsUrl_ResolvesToHome()
    {
        // Spec §5: the legacy settings DefaultPlaceUrl is vestigial and must be ignored by resolution.
        // No favorite default is configured (CreateLauncher wires favorites: null), but a legacy
        // settings value IS present — resolution must not fall back to it; it must resolve to Home.
        var (launcher, _, processStarter) = CreateLauncher(
            ticket: "TKT",
            defaultPlaceUrl: "920587237", // legacy settings default present — must be ignored
            startResult: 1);

        var result = await launcher.LaunchAsync(TestCookie, new LaunchTarget.DefaultGame());

        Assert.IsType<LaunchResult.Started>(result);
        // launchmode:app is a top-level, unescaped segment (mirrors LaunchAsync_UriIncludesAllRequiredSegments) —
        // NOT inside the escaped placelauncherurl payload, so plain string match, not Uri.EscapeDataString.
        Assert.Contains("launchmode:app", processStarter.LastUri);
        Assert.DoesNotContain("placelauncherurl", processStarter.LastUri);
        // The legacy settings place id must never surface in the launch URI.
        Assert.DoesNotContain(Uri.EscapeDataString("placeId=920587237"), processStarter.LastUri);
    }

    [Fact]
    public async Task LaunchAsync_TypedApi_DefaultGame_WithNoDefaultAnywhere_LaunchesHome()
    {
        var (launcher, _, processStarter) = CreateLauncher(ticket: "TKT", defaultPlaceUrl: null, startResult: 1);
        var result = await launcher.LaunchAsync(TestCookie, new LaunchTarget.DefaultGame());

        Assert.IsType<LaunchResult.Started>(result);
        // launchmode:app is a top-level, unescaped segment (mirrors LaunchAsync_UriIncludesAllRequiredSegments) —
        // NOT inside the escaped placelauncherurl payload, so plain string match, not Uri.EscapeDataString.
        Assert.Contains("launchmode:app", processStarter.LastUri);
        Assert.DoesNotContain("placelauncherurl", processStarter.LastUri);
    }

    [Fact]
    public void BuildAppLaunchUri_HasAppLaunchmode_NoPlaceLauncherUrl()
    {
        var uri = RobloxLauncher.BuildAppLaunchUri(
            ticket: "TKT-HOME", launchTime: 1714780000000, browserTrackerId: "1234567890123");

        Assert.Contains("launchmode:app", uri);
        Assert.Contains("gameinfo:TKT-HOME", uri);
        Assert.Contains("browsertrackerid:1234567890123", uri);
        Assert.DoesNotContain("placelauncherurl", uri);
        Assert.DoesNotContain("launchmode:play", uri);
    }

    [Fact]
    public async Task LaunchAsync_TypedApi_Home_BuildsAppLaunchUri()
    {
        var (launcher, _, processStarter) = CreateLauncher(ticket: "TKT", defaultPlaceUrl: null, startResult: 1);
        var result = await launcher.LaunchAsync(TestCookie, new LaunchTarget.Home());

        Assert.IsType<LaunchResult.Started>(result);
        Assert.Contains("launchmode:app", processStarter.LastUri);
        Assert.DoesNotContain("placelauncherurl", processStarter.LastUri);
        Assert.DoesNotContain("RequestGame", processStarter.LastUri);
    }

    [Fact]
    public async Task LaunchAsync_TypedApi_CookieExpired_ReturnsCookieExpired()
    {
        var api = new StubRobloxApi(_ => throw new CookieExpiredException());
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ => 1);
        var launcher = new RobloxLauncher(api, settings, processStarter);

        var result = await launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(42));

        Assert.IsType<LaunchResult.CookieExpired>(result);
    }

    [Fact]
    public async Task LaunchAsync_TypedApi_SessionLimited_ReturnsLimitedResult()
    {
        var api = new StubRobloxApi(_ => throw new SessionLimitedException());
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ => 1);
        var launcher = new RobloxLauncher(api, settings, processStarter);

        var result = await launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(42));

        Assert.IsType<LaunchResult.Limited>(result);
    }

    [Fact]
    public async Task LaunchAsync_TypedApi_RejectsEmptyCookie()
    {
        var (launcher, _, _) = CreateLauncher("T", TestPlaceUrl, startResult: 1);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            launcher.LaunchAsync("", new LaunchTarget.Place(1)));
    }

    [Fact]
    public async Task LaunchAsync_TypedApi_RejectsNullTarget()
    {
        var (launcher, _, _) = CreateLauncher("T", TestPlaceUrl, startResult: 1);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            launcher.LaunchAsync(TestCookie, target: null!));
    }

    // === Condition-based wait gate (probe threading) ===

    /// <summary>Counts probe calls and never reports a new pid, so any wait runs to its ceiling.</summary>
    private sealed class CountingProbe : IRobloxRunningProbe
    {
        public int Calls { get; private set; }
        public IReadOnlyList<int> GetRunningPlayerPids() { Calls++; return Array.Empty<int>(); }
        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers() => Array.Empty<RobloxProcessInfo>();
    }

    [Fact]
    public async Task LaunchAsync_CookieExpired_NeverWaitsForAClient()
    {
        // Only a successful launch produces a client to wait for. If a non-Started result waited,
        // a user without Roblox installed would eat the full 30s ceiling on every click.
        var api = new StubRobloxApi(_ => throw new CookieExpiredException());
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ => 1);
        var probe = new CountingProbe();
        var launcher = new RobloxLauncher(api, settings, processStarter, runningProbe: probe);

        var result = await launcher.LaunchAsync(TestCookie);

        Assert.IsType<LaunchResult.CookieExpired>(result);
        Assert.Equal(0, probe.Calls);   // never even snapshotted, let alone waited
    }

    [Fact]
    public async Task LaunchAsync_WithoutAProbe_StillCompletes_UnchangedBehaviour()
    {
        // The no-probe path must behave exactly as it did before this change, so every existing
        // call site and test that constructs a launcher without a probe keeps working.
        var api = new StubRobloxApi(_ => Task.FromResult(new AuthTicket("ticket", DateTimeOffset.UtcNow)));
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ => 4242);
        var launcher = new RobloxLauncher(api, settings, processStarter);   // no probe

        // Real-time ceiling per the established RobloxLauncherGateTests pattern: if a regression
        // made the no-probe path fall into WaitForNewClientAsync anyway (e.g. an inverted null
        // guard), the null probe's exceptions are swallowed internally and it would silently run to
        // the real 30s+1s ceiling instead of the ~250ms this path should take -- turning what should
        // be a fast test into a slow false-pass. Bounding the await converts that into a fast red
        // TimeoutException instead.
        var result = await launcher.LaunchAsync(TestCookie).WaitAsync(TimeSpan.FromSeconds(5));

        var started = Assert.IsType<LaunchResult.Started>(result);
        Assert.Equal(4242, started.Pid);
    }

    [Theory]
    [InlineData(true)]   // typed-target overload -- the ONLY path production code calls (MainViewModel.cs
                          // -- Squad Launch, the exact call site the 2026-08-01 wrong-FPS-cap bug was
                          // observed on). Prior to this fix-round, no test drove this overload with a
                          // probe at all -- deleting the gate wiring entirely would have shipped clean.
    [InlineData(false)]  // legacy placeUrl overload -- back-compat only, no production caller.
    public async Task LaunchAsync_WithProbeAndStartedResult_ActuallyPollsForTheClient(bool useTypedApi)
    {
        // Positive-path proof that the launcher wiring calls WaitForNewClientAsync when a probe is
        // present and the launch succeeds, on BOTH overloads independently -- each [InlineData] run
        // constructs its own launcher/probe and asserts on its own probe.Calls, so a break confined to
        // one overload's HoldForNewClientAsync call site fails that iteration specifically rather than
        // only proving the shared helper works in isolation. Neither of the other probe tests exercises
        // this at all: one asserts zero probe calls on a non-Started result, the other asserts unchanged
        // behaviour with no probe at all. Without this test, deleting the WaitForNewClientAsync call and
        // always falling through to the old Task.Delay(FFlagReadHold) -- even with a probe present --
        // would leave both other tests green.
        var clock = new FakeTimeProvider();
        var probe = new CountingProbe();
        var api = new StubRobloxApi(_ => Task.FromResult(new AuthTicket("T", DateTimeOffset.UtcNow)));
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ => 999);
        var launcher = new RobloxLauncher(
            api, settings, processStarter, clock, () => 1_000_000_000_000,
            favorites: null, clientAppSettings: null, globalBasicSettings: null, runningProbe: probe);

        var launchTask = useTypedApi
            ? launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(42))
            : launcher.LaunchAsync(TestCookie);

        // Drive the fake clock past several poll intervals to prove the gate is actually polling
        // (not skipping straight past to the old fixed 250ms hold). CountingProbe never reports a
        // new pid, so this only terminates once we advance past the full 30s ceiling below.
        for (var i = 0; i < 5; i++)
        {
            clock.Advance(RobloxLauncher.NewClientPollInterval);
            for (var pump = 0; pump < 50 && probe.Calls < i + 2; pump++)
            {
                await Task.Yield();
            }
        }
        Assert.True(probe.Calls >= 2,
            $"launcher did not poll the probe (stuck at {probe.Calls} calls) -- WaitForNewClientAsync does not appear to be wired into LaunchAsync (useTypedApi={useTypedApi})");

        clock.Advance(RobloxLauncher.NewClientWaitTimeout);

        var result = await launchTask.WaitAsync(TimeSpan.FromSeconds(5));
        var started = Assert.IsType<LaunchResult.Started>(result);
        Assert.Equal(999, started.Pid);
    }

    // === DI wiring (production registration shape) ===

    /// <summary>
    /// The gate logic above can be fully correct and fully unit-tested while the shipped app still
    /// runs the old fixed-delay path on every launch, if nothing ever hands the real
    /// <c>RobloxLauncher</c> a live probe -- that is exactly how the 2026-08-01 wrong-FPS-cap bug
    /// shipped: the wait primitive existed, both launch sites called it, and the object graph the
    /// app actually builds still resolved a null probe. This test exercises a REAL
    /// <see cref="IServiceProvider"/> built with the same registration shape App.xaml.cs's
    /// ConfigureServices uses for <c>IRobloxLauncher</c> -- an explicit factory that passes
    /// <c>sp.GetRequiredService&lt;IRobloxRunningProbe&gt;()</c> -- rather than hand-constructing a
    /// <see cref="RobloxLauncher"/> directly the way every other test in this file does. It does not
    /// call App.xaml.cs's ConfigureServices itself (that method also constructs real
    /// AppSettings/FavoriteGameStore/RobloxRunningProbe instances that touch disk and live Win32
    /// process state -- out of scope for a fast, deterministic unit test); it mirrors the one
    /// registration line this task changed. A regression to the bare
    /// <c>AddSingleton&lt;IRobloxLauncher, RobloxLauncher&gt;()</c> form would NOT fail this test
    /// (verified empirically -- see task-3-report.md -- the built-in container still auto-resolves a
    /// registered optional ctor parameter), but a factory that drops or hardcodes
    /// <c>runningProbe: null</c> -- the actual shape of the historical bug -- does.
    /// </summary>
    [Fact]
    public void ProductionDiRegistration_ThreadsTheLiveRunningProbeIntoTheLauncher()
    {
        var services = new ServiceCollection();
        var probe = new CountingProbe();
        services.AddSingleton<IRobloxApi>(
            new StubRobloxApi(_ => Task.FromResult(new AuthTicket("T", DateTimeOffset.UtcNow))));
        services.AddSingleton<IAppSettings>(new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl });
        services.AddSingleton<IProcessStarter>(new RecordingProcessStarter(_ => 1));
        services.AddSingleton<IRobloxRunningProbe>(probe);

        // Mirrors src/ROROROblox.App/App.xaml.cs's IRobloxLauncher registration verbatim in shape.
        services.AddSingleton<IRobloxLauncher>(sp => new RobloxLauncher(
            sp.GetRequiredService<IRobloxApi>(),
            sp.GetRequiredService<IAppSettings>(),
            sp.GetRequiredService<IProcessStarter>(),
            favorites: sp.GetService<IFavoriteGameStore>(),
            clientAppSettings: sp.GetService<IClientAppSettingsWriter>(),
            globalBasicSettings: sp.GetService<IGlobalBasicSettingsWriter>(),
            runningProbe: sp.GetRequiredService<IRobloxRunningProbe>()));

        using var provider = services.BuildServiceProvider();
        var launcher = provider.GetRequiredService<IRobloxLauncher>();

        var field = typeof(RobloxLauncher).GetField("_runningProbe", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var wiredProbe = field!.GetValue(launcher);

        // Same instance, not merely non-null -- proves the SAME singleton StartupGate resolves is
        // the one the launcher got, not a second instance from a duplicate registration.
        Assert.Same(probe, wiredProbe);
    }

    // === Helpers ===

    private static (RobloxLauncher, InMemoryAppSettings, RecordingProcessStarter) CreateLauncher(
        string ticket,
        string? defaultPlaceUrl,
        int startResult = 1,
        Exception? startThrows = null,
        IClientAppSettingsWriter? clientAppSettings = null,
        IRobloxRunningProbe? runningProbe = null)
    {
        var api = new StubRobloxApi(_ => Task.FromResult(new AuthTicket(ticket, DateTimeOffset.UtcNow)));
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = defaultPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ =>
        {
            if (startThrows is not null) throw startThrows;
            return startResult;
        });
        var launcher = new RobloxLauncher(
            api, settings, processStarter,
            favorites: null, clientAppSettings: clientAppSettings, runningProbe: runningProbe);
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
        public Task<IReadOnlyList<GameSearchResult>> SearchGamesAsync(string query) => throw new NotImplementedException();
        public Task<IReadOnlyList<Friend>> GetFriendsAsync(string cookie, long userId) => throw new NotImplementedException();
        public Task<IReadOnlyList<UserPresence>> GetPresenceAsync(string cookie, IEnumerable<long> userIds) => throw new NotImplementedException();
        public Task<ShareLinkResolution?> ResolveShareLinkAsync(string cookie, string code, string linkType) => throw new NotImplementedException();
    }

    private sealed class InMemoryAppSettings : IAppSettings
    {
        public string? DefaultPlaceUrl { get; set; }
        public bool LaunchMainOnStartup { get; set; }
        public string? ActiveThemeId { get; set; }

        public Task<string?> GetDefaultPlaceUrlAsync() => Task.FromResult(DefaultPlaceUrl);
        public Task SetDefaultPlaceUrlAsync(string url) { DefaultPlaceUrl = url; return Task.CompletedTask; }
        public Task<bool> GetLaunchMainOnStartupAsync() => Task.FromResult(LaunchMainOnStartup);
        public Task SetLaunchMainOnStartupAsync(bool enabled) { LaunchMainOnStartup = enabled; return Task.CompletedTask; }
        public Task<string?> GetActiveThemeIdAsync() => Task.FromResult(ActiveThemeId);
        public Task SetActiveThemeIdAsync(string themeId) { ActiveThemeId = themeId; return Task.CompletedTask; }
        public bool BloxstrapWarningDismissed { get; set; }
        public Task<bool> GetBloxstrapWarningDismissedAsync() => Task.FromResult(BloxstrapWarningDismissed);
        public Task SetBloxstrapWarningDismissedAsync(bool value) { BloxstrapWarningDismissed = value; return Task.CompletedTask; }
        public bool MuteIdleAlerts { get; set; }
        public Task<bool> GetMuteIdleAlertsAsync() => Task.FromResult(MuteIdleAlerts);
        public Task SetMuteIdleAlertsAsync(bool muted) { MuteIdleAlerts = muted; return Task.CompletedTask; }
        public int IdleWarnThresholdMinutes { get; set; } = 15;
        public Task<int> GetIdleWarnThresholdMinutesAsync() => Task.FromResult(IdleWarnThresholdMinutes <= 0 ? 15 : IdleWarnThresholdMinutes);
        public Task SetIdleWarnThresholdMinutesAsync(int minutes) { IdleWarnThresholdMinutes = minutes <= 0 ? 15 : minutes; return Task.CompletedTask; }
        public bool CarefulSquadLaunch { get; set; }
        public Task<bool> GetCarefulSquadLaunchAsync() => Task.FromResult(CarefulSquadLaunch);
        public Task SetCarefulSquadLaunchAsync(bool careful) { CarefulSquadLaunch = careful; return Task.CompletedTask; }
        public bool StreamerMode { get; set; }
        public Task<bool> GetStreamerModeAsync() => Task.FromResult(StreamerMode);
        public Task SetStreamerModeAsync(bool enabled) { StreamerMode = enabled; return Task.CompletedTask; }
        public bool MemoryWatchdogEnabled { get; set; } = true;
        public Task<bool> GetMemoryWatchdogEnabledAsync() => Task.FromResult(MemoryWatchdogEnabled);
        public Task SetMemoryWatchdogEnabledAsync(bool enabled) { MemoryWatchdogEnabled = enabled; return Task.CompletedTask; }
        public int? MemoryReserveMb { get; set; }
        public Task<int?> GetMemoryReserveMbAsync() => Task.FromResult(MemoryReserveMb);
        public Task SetMemoryReserveMbAsync(int? reserveMb) { MemoryReserveMb = reserveMb; return Task.CompletedTask; }
        public int? MemoryCapMb { get; set; }
        public Task<int?> GetMemoryCapMbAsync() => Task.FromResult(MemoryCapMb);
        public Task SetMemoryCapMbAsync(int? capMb) { MemoryCapMb = capMb; return Task.CompletedTask; }
        public int ProjectionWarnMinutes { get; set; } = 120;
        public Task<int> GetProjectionWarnMinutesAsync() => Task.FromResult(ProjectionWarnMinutes);
        public Task SetProjectionWarnMinutesAsync(int minutes) { ProjectionWarnMinutes = minutes; return Task.CompletedTask; }
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

    // === Sequencing / semaphore ===

    [Fact]
    public async Task LaunchAsync_TwoConcurrentCalls_AreSerialized()
    {
        var writeOrder = new List<int>();
        var writer = new RecordingWriter(writeOrder);
        var (launcher, _, _) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1,
            clientAppSettings: writer);

        var firstTask = launcher.LaunchAsync("cookie-a", placeUrl: null, fpsCap: 30);
        var secondTask = launcher.LaunchAsync("cookie-b", placeUrl: null, fpsCap: 144);

        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(new[] { 30, 144 }, writeOrder);
    }

    private sealed class RecordingWriter(List<int> writeOrder) : IClientAppSettingsWriter
    {
        public Task WriteFpsAsync(int? fps, CancellationToken ct = default)
        {
            if (fps.HasValue) writeOrder.Add(fps.Value);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Reports a new pid only after <paramref name="callsBeforeAppearing"/> polls, and appends a
    /// sentinel to the shared timeline when it does. Lets a test assert that the client appeared
    /// BETWEEN the two settings writes rather than after both.
    /// </summary>
    private sealed class AppearAfterProbe(List<int> timeline, int callsBeforeAppearing) : IRobloxRunningProbe
    {
        private int _calls;
        public IReadOnlyList<int> GetRunningPlayerPids()
        {
            _calls++;
            if (_calls < callsBeforeAppearing) return Array.Empty<int>();
            if (_calls == callsBeforeAppearing) timeline.Add(-1);   // -1 == "client appeared"
            return new[] { 999 };
        }
        public IReadOnlyList<RobloxProcessInfo> GetRunningPlayers() => Array.Empty<RobloxProcessInfo>();
    }

    [Fact]
    public async Task TwoSequentialLaunches_SecondWriteHappensOnlyAfterTheFirstClientAppears()
    {
        // The shipped bug (observed 2026-08-01): account A configured Unlimited (9999) launched
        // ~1s before account B configured 20. A ran at 20, because B's write landed before A's
        // client had read the file. The old hold was 250ms measured from Process.Start returning
        // on a protocol URI — before RobloxPlayerBeta even exists.
        var timeline = new List<int>();
        var writer = new RecordingWriter(timeline);
        var probe = new AppearAfterProbe(timeline, callsBeforeAppearing: 2);
        var (launcher, _, _) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1,
            clientAppSettings: writer,
            runningProbe: probe);

        await launcher.LaunchAsync("cookie-a", placeUrl: null, fpsCap: 9999);
        await launcher.LaunchAsync("cookie-b", placeUrl: null, fpsCap: 20);

        // Ordering is the assertion, not merely that both values were written:
        //   9999 written -> client appeared (-1) -> 20 written
        Assert.Equal(new[] { 9999, -1, 20 }, timeline);
    }
}
