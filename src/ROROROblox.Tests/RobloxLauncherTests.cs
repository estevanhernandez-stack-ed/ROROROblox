using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    // === Settling the FPS cap before launch (Task 3: settingsProbe threading) ===

    /// <summary>Settings probe whose reported cap the test controls.</summary>
    private sealed class StubSettingsProbe : IGlobalBasicSettingsProbe
    {
        public int? Cap { get; set; }
        public int ReadCalls { get; private set; }
        public int? ReadFramerateCap() { ReadCalls++; return Cap; }
        public DateTimeOffset? GetLastWriteTimeUtc() => DateTimeOffset.UnixEpoch;
    }

    /// <summary>Records every FramerateCap write in order.</summary>
    private sealed class RecordingGlobalBasicWriter : IGlobalBasicSettingsWriter
    {
        public List<int?> Writes { get; } = new();
        public Task WriteFramerateCapAsync(int? fps, CancellationToken ct = default)
        {
            Writes.Add(fps);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Reports <paramref name="before"/> on its first call (SettleAsync's fast-path pre-check) and
    /// <paramref name="after"/> on every call thereafter (the post-write confirm read) -- models "the
    /// write landed" deterministically, by call count rather than by wall-clock timing.
    /// <para>
    /// <see cref="GetLastWriteTimeUtc"/> reports a FIXED but RECENT instant (the caller supplies it
    /// -- the ordering test below passes the fake clock's own start time) rather than
    /// <see cref="DateTimeOffset.UnixEpoch"/>. That distinction matters: an epoch mtime is decades
    /// stale against a <c>FakeTimeProvider</c>'s 2000-01-01 default, so <c>FpsCapSettler</c>'s
    /// pre-write quiet-wait credits "already quiet" on its very first check and never awaits a real
    /// <c>Task.Delay</c> -- which means dropping the <c>await</c> on <c>ApplyFpsCapAsync</c> at the
    /// call site is invisible to a test built on that probe (confirmed empirically: an
    /// epoch-mtime version of this fixture let all <see cref="FpsCapSettler.MaxWriteAttempts"/>
    /// writes land inside one synchronous continuation chain, before any pump loop's first
    /// iteration ever ran, dropped-await or not). A fixed RECENT mtime makes the pre-write
    /// quiet-wait a genuine suspension point on the fake clock -- the test must
    /// <c>clock.Advance</c> past <see cref="FpsCapSettler.QuietDebounce"/> for it to release --
    /// which is exactly the happens-before edge a dropped <c>await</c> would break (fix round 1,
    /// escalated from Minor: see the report for the mutation proof both ways).
    /// </para>
    /// </summary>
    private sealed class FlipAfterFirstReadProbe : IGlobalBasicSettingsProbe
    {
        private readonly int _before;
        private readonly int _after;
        private readonly DateTimeOffset _lastWriteTimeUtc;
        public int ReadCalls { get; private set; }
        public FlipAfterFirstReadProbe(int before, int after, DateTimeOffset lastWriteTimeUtc)
        {
            _before = before;
            _after = after;
            _lastWriteTimeUtc = lastWriteTimeUtc;
        }
        public int? ReadFramerateCap() { ReadCalls++; return ReadCalls == 1 ? _before : _after; }
        public DateTimeOffset? GetLastWriteTimeUtc() => _lastWriteTimeUtc;
    }

    [Fact]
    public async Task LaunchAsync_WhenTheFileAlreadyHoldsTheCap_WritesNothingAndDoesNotWait()
    {
        var probe = new StubSettingsProbe { Cap = 20 };
        var gbs = new RecordingGlobalBasicWriter();
        var (launcher, _, _) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1,
            globalBasicSettings: gbs,
            settingsProbe: probe);

        var result = await launcher
            .LaunchAsync(TestCookie, new LaunchTarget.Place(42), fpsCap: 20)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<LaunchResult.Started>(result);
        Assert.Empty(gbs.Writes);          // fast path: nothing written
        Assert.Equal(1, probe.ReadCalls);  // and nothing waited for
    }

    [Fact]
    public async Task LaunchAsync_WhenTheCapDiffers_WritesItBeforeStartingTheProcess()
    {
        // Probe reports the old value on the fast-path pre-check, then the new value on the
        // post-write confirm, so the settle succeeds on attempt 1 with exactly one write. The
        // mtime it reports is a FIXED but RECENT instant (the fake clock's own start time, captured
        // before any Advance) -- not UnixEpoch -- so the pre-write quiet-wait is a genuine
        // suspension point on the fake clock that this test must drive forward. See
        // FlipAfterFirstReadProbe's remarks: fix round 1 escalated this from Minor because an
        // epoch-mtime version of this test could not fail when the production `await` on
        // ApplyFpsCapAsync was dropped -- SettleAsync resolved in one synchronous continuation chain
        // either way, so the write always landed before Process.Start regardless of whether the
        // call site actually awaited it.
        var clock = new FakeTimeProvider();
        var recentMtime = clock.GetUtcNow();   // "just written", not decades stale
        var probe = new FlipAfterFirstReadProbe(before: 9999, after: 20, lastWriteTimeUtc: recentMtime);
        var gbs = new RecordingGlobalBasicWriter();
        var starter = new OrderRecordingStarter(gbs);
        var (launcher, _, _) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            startResult: 1,
            globalBasicSettings: gbs,
            settingsProbe: probe,
            processStarter: starter,
            timeProvider: clock);

        var task = launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(42), fpsCap: 20);

        // Drive the fake clock past QuietDebounce so the pre-write quiet-wait releases. Pumped
        // between advances so the poll loop's continuation actually reaches its next await and
        // arms a fresh timer against the still-advancing clock -- a bare loop of Advance() calls
        // with no yields would race the continuation (same reasoning the retired
        // RobloxLauncherGateTests.AdvancePastPollAsync documented for the pid-based gate this
        // mechanism replaced). Bounded at 60 iterations of QuietPollInterval (6s of fake time) for
        // margin over the 5s QuietDebounce; the loop exits the moment the write lands.
        for (var i = 0; i < 60 && gbs.Writes.Count == 0; i++)
        {
            clock.Advance(FpsCapSettler.QuietPollInterval);
            await Task.Yield();
        }

        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<LaunchResult.Started>(result);
        Assert.Equal(new int?[] { 20 }, gbs.Writes);
        // The whole point: the cap is on disk before the client exists.
        Assert.True(starter.WriteCountAtStart == 1,
            $"expected the cap written before Process.Start, saw {starter.WriteCountAtStart} writes at start");
    }

    /// <summary>
    /// Captures every log entry, keyed by level and formatted message. Fakes duplicated per-file so
    /// each test file stands alone -- see Task 3 / MemoryWatchdogLoggingTests.cs for the same pattern.
    /// </summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? ex,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, ex)));
    }

    [Fact]
    public async Task LaunchAsync_WithWriterButNoProbe_StillWritesButLogsALoudWarning()
    {
        // Not reachable in the shipped app (App.xaml.cs always resolves IGlobalBasicSettingsProbe
        // alongside IGlobalBasicSettingsWriter via GetRequiredService), but a future caller who
        // wires a writer without a probe -- a second registration, a plugin host, an integration
        // harness -- must get a LOUD degrade, not the silent one that shipped the 2026-08-01
        // wrong-cap bug. Fix round 1, Important: this branch existed and was exercised by the two
        // tests above (both of which also pass a probe) but had no test proving the no-probe path
        // itself, nor that the degrade is visible anywhere.
        var api = new StubRobloxApi(_ => Task.FromResult(new AuthTicket("T", DateTimeOffset.UtcNow)));
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl };
        var processStarter = new RecordingProcessStarter(_ => 1);
        var gbs = new RecordingGlobalBasicWriter();
        var log = new CapturingLogger<RobloxLauncher>();
        var launcher = new RobloxLauncher(
            api, settings, processStarter,
            favorites: null, clientAppSettings: null,
            globalBasicSettings: gbs, settingsProbe: null, logger: log);

        var result = await launcher
            .LaunchAsync(TestCookie, new LaunchTarget.Place(42), fpsCap: 20)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<LaunchResult.Started>(result);
        Assert.Equal(new int?[] { 20 }, gbs.Writes);   // the write itself still happens
        Assert.Contains(log.Entries, e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("No IGlobalBasicSettingsProbe wired", StringComparison.Ordinal));
    }

    /// <summary>
    /// Time-aware settings-file double for the launch-baseline test below. Unlike
    /// <see cref="StubSettingsProbe"/> (fixed cap) or <see cref="FlipAfterFirstReadProbe"/>
    /// (call-count scripted), this holds LIVE mutable state so a test can move its mtime at a
    /// specific FAKE-CLOCK instant to model a DIFFERENT process writing to the file -- the shape
    /// FpsCapSettlerTests.TimeAwareProbe uses at the settler level, duplicated here per this file's
    /// stand-alone convention (see CapturingLogger's remarks above) because this test exercises the
    /// baseline threading THROUGH RobloxLauncher across two consecutive launches, not the settler in
    /// isolation.
    /// </summary>
    private sealed class TimeAwareLauncherProbe : IGlobalBasicSettingsProbe
    {
        public int? Cap { get; set; }
        public DateTimeOffset? Mtime { get; set; }
        public int? ReadFramerateCap() => Cap;
        public DateTimeOffset? GetLastWriteTimeUtc() => Mtime;
    }

    /// <summary>Writes through to a <see cref="TimeAwareLauncherProbe"/>, stamping its own write's mtime.</summary>
    private sealed class TimeAwareLauncherWriter : IGlobalBasicSettingsWriter
    {
        private readonly TimeAwareLauncherProbe _probe;
        private readonly TimeProvider _clock;
        public List<int?> Writes { get; } = new();

        public TimeAwareLauncherWriter(TimeAwareLauncherProbe probe, TimeProvider clock)
        {
            _probe = probe;
            _clock = clock;
        }

        public Task WriteFramerateCapAsync(int? fps, CancellationToken ct = default)
        {
            Writes.Add(fps);
            _probe.Cap = fps;
            _probe.Mtime = _clock.GetUtcNow();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// End-to-end proof that <see cref="RobloxLauncher"/> itself -- not just
    /// <see cref="FpsCapSettler"/> in isolation -- threads the launch baseline correctly. Account A
    /// launches with no cap of its own (only its <c>Process.Start</c> matters, to seed the baseline
    /// <see cref="RobloxLauncher"/> must remember). Account B launches next wanting a DIFFERENT cap;
    /// its settle call must refuse to write B's cap until A's client proves it read A's cap first (a
    /// write-back to the same file), not credit "the file hasn't moved since A launched" as quiet.
    /// This is the exact end-to-end shape of the measured bug: three accounts at three different
    /// caps each came up running the next account's value.
    /// </summary>
    [Fact]
    public async Task LaunchAsync_SecondLaunch_WaitsForFirstLaunchedClientsWriteBeforeApplyingTheNextCap()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var probe = new TimeAwareLauncherProbe { Cap = 9999, Mtime = clock.GetUtcNow() };
        var gbs = new TimeAwareLauncherWriter(probe, clock);
        var starter = new RecordingProcessStarter(_ => 1);
        var (launcher, _, _) = CreateLauncher(
            ticket: "T",
            defaultPlaceUrl: TestPlaceUrl,
            globalBasicSettings: gbs,
            settingsProbe: probe,
            processStarter: starter,
            timeProvider: clock);

        // Launch A -- no cap requested, so nothing is written; only Process.Start (and the baseline
        // it seeds) matters here.
        var firstResult = await launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(1))
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<LaunchResult.Started>(firstResult);
        Assert.Empty(gbs.Writes);

        // Launch B wants cap 20; the probe currently reports 9999 -- the slow path.
        var secondTask = launcher.LaunchAsync(TestCookie, new LaunchTarget.Place(2), fpsCap: 20);

        // Drive the clock well past QuietDebounce with the file's mtime UNCHANGED since A's launch
        // -- exactly the pre-fix bug shape. Must NOT write B's cap while this holds.
        var noWriteWindow = FpsCapSettler.QuietDebounce + TimeSpan.FromSeconds(3);
        var elapsed = TimeSpan.Zero;
        while (elapsed < noWriteWindow)
        {
            clock.Advance(FpsCapSettler.QuietPollInterval);
            elapsed += FpsCapSettler.QuietPollInterval;
            for (var i = 0; i < 8; i++) { await Task.Yield(); }
        }
        Assert.Empty(gbs.Writes);

        // A's client finally writes its own value back -- proof it read the file. Mutated directly
        // on the probe (not through `gbs`, which only records OUR writes): this models the OTHER
        // process.
        probe.Cap = 9999;
        probe.Mtime = clock.GetUtcNow();

        elapsed = TimeSpan.Zero;
        var settleBudget = FpsCapSettler.SettleTimeout + TimeSpan.FromSeconds(2);
        while (elapsed < settleBudget && !secondTask.IsCompleted)
        {
            clock.Advance(FpsCapSettler.QuietPollInterval);
            elapsed += FpsCapSettler.QuietPollInterval;
            for (var i = 0; i < 8; i++) { await Task.Yield(); }
        }

        var secondResult = await secondTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<LaunchResult.Started>(secondResult);
        Assert.Equal(new int?[] { 20 }, gbs.Writes);
    }

    /// <summary>Captures how many cap writes had happened at the moment Process.Start was called.</summary>
    private sealed class OrderRecordingStarter : IProcessStarter
    {
        private readonly RecordingGlobalBasicWriter _writer;
        public int WriteCountAtStart { get; private set; } = -1;
        public OrderRecordingStarter(RecordingGlobalBasicWriter writer) => _writer = writer;
        public int StartViaShell(string fileNameOrUri)
        {
            WriteCountAtStart = _writer.Writes.Count;
            return 1;
        }
    }

    // === DI wiring (production registration shape) ===

    /// <summary>
    /// The settle logic above can be fully correct and fully unit-tested while the shipped app
    /// still writes the cap with no probe wired at all, if nothing ever hands the real
    /// <c>RobloxLauncher</c> a live <see cref="IGlobalBasicSettingsProbe"/> -- that is exactly how
    /// the 2026-08-01 wrong-FPS-cap bug shipped once before: the mechanism existed, both launch
    /// sites called it, and the object graph the app actually builds still resolved a null
    /// dependency.
    ///
    /// This test calls the REAL <see cref="App.ConfigureServices"/> (made <c>internal</c> for this
    /// purpose -- <c>InternalsVisibleTo("ROROROblox.Tests")</c> already existed in
    /// ROROROblox.App.csproj) against a real <see cref="ServiceCollection"/>, then uses
    /// <see cref="ServiceCollectionServiceExtensions"/>'s <c>Replace</c> to swap the side-effecting
    /// descriptors (<see cref="IRobloxApi"/>, <see cref="IAppSettings"/>, <see cref="IProcessStarter"/>,
    /// <see cref="IGlobalBasicSettingsProbe"/>) for test doubles BEFORE building the provider or
    /// resolving anything -- so nothing in this test ever constructs the real <c>RobloxApi</c>
    /// (would need a working HttpClient), <c>AppSettings</c>/<c>ProcessStarter</c> (Win32/OS calls),
    /// or <c>GlobalBasicSettingsProbe</c> (live file reads). Every OTHER registration
    /// ConfigureServices makes is either a parameterless <c>AddSingleton&lt;T,U&gt;</c> or a factory
    /// lambda -- both deferred to resolve time -- and DI is lazy, so resolving only
    /// <see cref="IRobloxLauncher"/> constructs only its own transitive dependency chain, not the
    /// whole app graph (no <c>MainViewModel</c>, no <c>MainWindow</c>, nothing WPF-affined). The
    /// remaining chain members that DO get constructed for real --
    /// <see cref="IFavoriteGameStore"/>/<see cref="IClientAppSettingsWriter"/>/
    /// <see cref="IGlobalBasicSettingsWriter"/> -- were confirmed to do no I/O in their constructors
    /// (all disk access lives in their async methods, never called here) before this test was written.
    /// A throwaway <see cref="NullLoggerFactory"/> stands in for the real file-backed one
    /// ConfigureServices would otherwise register.
    /// </summary>
    [Fact]
    public void ProductionDiRegistration_ThreadsTheLiveSettingsProbeIntoTheLauncher()
    {
        var services = new ServiceCollection();
        // Fully qualified (not `using ROROROblox.App;` + `App.ConfigureServices`): the namespace
        // ROROROblox.App and the class ROROROblox.App.App share a name, and the bare form is
        // ambiguous to the compiler (CS0234, "ConfigureServices does not exist in the namespace
        // ROROROblox.App" -- it tried to resolve App.ConfigureServices as a nested namespace path).
        global::ROROROblox.App.App.ConfigureServices(services, NullLoggerFactory.Instance);

        var probe = new StubSettingsProbe { Cap = 20 };
        services.Replace(ServiceDescriptor.Singleton<IRobloxApi>(
            new StubRobloxApi(_ => Task.FromResult(new AuthTicket("T", DateTimeOffset.UtcNow)))));
        services.Replace(ServiceDescriptor.Singleton<IAppSettings>(
            new InMemoryAppSettings { DefaultPlaceUrl = TestPlaceUrl }));
        services.Replace(ServiceDescriptor.Singleton<IProcessStarter>(new RecordingProcessStarter(_ => 1)));
        services.Replace(ServiceDescriptor.Singleton<IGlobalBasicSettingsProbe>(probe));

        using var provider = services.BuildServiceProvider();
        var launcher = provider.GetRequiredService<IRobloxLauncher>();

        var field = typeof(RobloxLauncher).GetField("_settingsProbe", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        var wiredProbe = field!.GetValue(launcher);

        // Same instance, not merely non-null -- proves the SAME singleton the DI container resolves
        // is the one the launcher got, not a second instance from a duplicate registration.
        Assert.Same(probe, wiredProbe);
    }

    // === Helpers ===

    private static (RobloxLauncher, InMemoryAppSettings, RecordingProcessStarter) CreateLauncher(
        string ticket,
        string? defaultPlaceUrl,
        int startResult = 1,
        Exception? startThrows = null,
        IClientAppSettingsWriter? clientAppSettings = null,
        IGlobalBasicSettingsWriter? globalBasicSettings = null,
        IGlobalBasicSettingsProbe? settingsProbe = null,
        IProcessStarter? processStarter = null,
        TimeProvider? timeProvider = null)
    {
        var api = new StubRobloxApi(_ => Task.FromResult(new AuthTicket(ticket, DateTimeOffset.UtcNow)));
        var settings = new InMemoryAppSettings { DefaultPlaceUrl = defaultPlaceUrl };
        var recordingStarter = new RecordingProcessStarter(_ =>
        {
            if (startThrows is not null) throw startThrows;
            return startResult;
        });
        // Callers driving the write-before-launch ordering assertion (OrderRecordingStarter) supply
        // their own IProcessStarter and discard this method's returned RecordingProcessStarter --
        // recordingStarter is still built unconditionally so every pre-existing call site (none of
        // which pass processStarter) keeps its return-tuple shape and behaviour unchanged.
        var starterToUse = processStarter ?? recordingStarter;
        // timeProvider is null for every pre-existing call site (unchanged behaviour: the public
        // ctor, real TimeProvider.System). Callers that need a FakeTimeProvider -- to avoid paying
        // real wall-clock time for FpsCapSettler's quiet-wait constants -- pass one explicitly and
        // get routed through the internal test ctor.
        var launcher = timeProvider is null
            ? new RobloxLauncher(
                api, settings, starterToUse,
                favorites: null, clientAppSettings: clientAppSettings,
                globalBasicSettings: globalBasicSettings, settingsProbe: settingsProbe)
            : new RobloxLauncher(
                api, settings, starterToUse, timeProvider, () => 1_000_000_000_000,
                favorites: null, clientAppSettings: clientAppSettings,
                globalBasicSettings: globalBasicSettings, settingsProbe: settingsProbe);
        return (launcher, settings, recordingStarter);
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
        public string? DismissedFpsCapWarningSignature { get; set; }
        public Task<string?> GetDismissedFpsCapWarningSignatureAsync() => Task.FromResult(DismissedFpsCapWarningSignature);
        public Task SetDismissedFpsCapWarningSignatureAsync(string? signature) { DismissedFpsCapWarningSignature = signature; return Task.CompletedTask; }
        public bool MuteIdleAlerts { get; set; }
        public Task<bool> GetMuteIdleAlertsAsync() => Task.FromResult(MuteIdleAlerts);
        public Task SetMuteIdleAlertsAsync(bool muted) { MuteIdleAlerts = muted; return Task.CompletedTask; }
        public int IdleWarnThresholdMinutes { get; set; } = 15;
        public Task<int> GetIdleWarnThresholdMinutesAsync() => Task.FromResult(IdleWarnThresholdMinutes <= 0 ? 15 : IdleWarnThresholdMinutes);
        public Task SetIdleWarnThresholdMinutesAsync(int minutes) { IdleWarnThresholdMinutes = minutes <= 0 ? 15 : minutes; return Task.CompletedTask; }
        public bool CarefulSquadLaunch { get; set; }
        public Task<bool> GetCarefulSquadLaunchAsync() => Task.FromResult(CarefulSquadLaunch);
        public Task<bool> GetAlwaysShowRecycleAsync() => Task.FromResult(false);
        public Task SetAlwaysShowRecycleAsync(bool always) => throw new NotImplementedException();
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

}
