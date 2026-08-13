using System.Net;
using System.Text;
using ROROROblox.Core.Diagnostics;

namespace ROROROblox.Tests;

/// <summary>
/// Coverage of <see cref="RobloxUpdateProbe"/> — the v1.7.0 install-deferral foundational signal
/// (spec §"Components > 1. Update-pending detection"). Both members are degrade-safe: ANY failure
/// returns the "don't block the launch" answer (false). No live network — the CDN GET is stubbed
/// via <see cref="StubHttpHandler"/>; the process scan and installed-version read are injected seams.
/// </summary>
public class RobloxUpdateProbeTests
{
    private const string ClientVersionUrl =
        "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string VersionJson(string version) =>
        $$"""{"version":"{{version}}","clientVersionUpload":"version-abc123","bootstrapperVersion":"1.0.0"}""";

    // ---- IsInstallerRunning ------------------------------------------------

    [Fact]
    public void IsInstallerRunning_True_WhenInjectedScanReportsProcessPresent()
    {
        var probe = new RobloxUpdateProbe(
            installerRunning: () => true,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(new StubHttpHandler()));

        Assert.True(probe.IsInstallerRunning());
    }

    [Fact]
    public void IsInstallerRunning_False_WhenInjectedScanReportsProcessAbsent()
    {
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(new StubHttpHandler()));

        Assert.False(probe.IsInstallerRunning());
    }

    [Fact]
    public void IsInstallerRunning_False_WhenScanThrows()
    {
        var probe = new RobloxUpdateProbe(
            installerRunning: () => throw new InvalidOperationException("scan blew up"),
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(new StubHttpHandler()));

        Assert.False(probe.IsInstallerRunning());
    }

    // ---- IsUpdatePendingAsync ----------------------------------------------

    [Fact]
    public async Task IsUpdatePendingAsync_True_WhenInstalledDiffersFromLatest()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("2.662.0.6620000")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.True(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenInstalledEqualsLatest()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("2.661.0.6610701")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenInstalledEqualsLatest_IgnoringCaseAndWhitespace()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson(" 2.661.0.6610701 ")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    // ---- Degrade-safe: never returns true on failure -----------------------

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenLatestFetchReturnsNon200()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenLatestFetchThrows()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(_ => throw new HttpRequestException("network down"));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenCdnJsonIsMalformed()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, "this is not json {{{"));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenCdnVersionFieldMissing()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, """{"clientVersionUpload":"version-abc123"}"""));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenCdnVersionFieldEmpty()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenInstalledVersionNull_NoInstall()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("2.662.0.6620000")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => null,
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenInstalledVersionProviderThrows()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("2.662.0.6620000")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => throw new InvalidOperationException("disk read failed"),
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    // ---- UA contract -------------------------------------------------------

    [Fact]
    public async Task IsUpdatePendingAsync_CdnGet_UsesRororoUserAgent_NoBrowserSpoof()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("2.661.0.6610701")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        await probe.IsUpdatePendingAsync();

        var req = Assert.Single(stub.Requests);
        var ua = req.Headers.UserAgent.ToString();
        Assert.Contains("RORORO", ua);
        Assert.DoesNotContain("Mozilla", ua);
    }

    [Fact]
    public async Task IsUpdatePendingAsync_CdnGet_HitsDocumentedClientVersionEndpoint()
    {
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("2.661.0.6610701")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        await probe.IsUpdatePendingAsync();

        var req = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal(ClientVersionUrl, req.RequestUri?.ToString());
    }

    // ---- F-104: the handler's version is the one that will actually run -----

    [Fact]
    public async Task IsUpdatePendingAsync_True_WhenHandlerIsStale_EvenThoughNewestInstallIsCurrent()
    {
        // THE F-104 REGRESSION, with the numbers measured off a live box 2026-08-12.
        // The handler was pinned to version-082eb75e16714844 at 0,733,448 while
        // version-ddf602d9cfe44005 at 0,734,0 sat newer on disk and matched the CDN. Reading only
        // the newest install, the gate answered "no update pending" and released the batch — then
        // every client discovered independently that IT was stale and self-updated at once.
        // Before the fix this assertion reads False.
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("0, 734, 0, 7340917")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "0, 734, 0, 7340917",   // newest install: current
            handlerVersionProvider: () => "0, 733, 448, 7332252",   // what will actually run: stale
            httpClient: new HttpClient(stub));

        Assert.True(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenHandlerAndNewestInstallBothMatchLatest()
    {
        // The window closed: Roblox repointed the handler at the new version. Measured on the same
        // box ~25 minutes later, which is why this defect reads as random rather than reproducible.
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("0, 734, 0, 7340917")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "0, 734, 0, 7340917",
            handlerVersionProvider: () => "0, 734, 0, 7340917",
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_True_WhenNewestInstallIsStale_ButHandlerIsCurrent()
    {
        // The mirror case. An old version-* folder still on disk must not be ignored either — the
        // gate holds when EITHER read disagrees, so neither side can quietly wave a batch through.
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("0, 734, 0, 7340917")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "0, 733, 603, 7330990",
            handlerVersionProvider: () => "0, 734, 0, 7340917",
            httpClient: new HttpClient(stub));

        Assert.True(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_UnreadableHandler_DoesNotVote_InstalledStillDecides()
    {
        // A strap owns the handler, or the registry is locked down. "Unknown" is not "agrees" —
        // the readable side decides alone rather than the unknown side forcing a false negative.
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("0, 734, 0, 7340917")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "0, 734, 0, 7340917",
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenHandlerProviderThrows_AndInstalledMatches()
    {
        // Degrade-safe: a registry blow-up must never be the reason a batch stalls.
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("0, 734, 0, 7340917")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "0, 734, 0, 7340917",
            handlerVersionProvider: () => throw new InvalidOperationException("registry locked"),
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
    }

    [Fact]
    public async Task IsUpdatePendingAsync_False_WhenNeitherVersionIsReadable_NoInstall()
    {
        // Nothing local to compare. Item 9 owns the "Roblox not installed" surface; the probe just
        // declines to block, and does not even spend the CDN round-trip.
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Json(HttpStatusCode.OK, VersionJson("0, 734, 0, 7340917")));
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => null,
            handlerVersionProvider: () => null,
            httpClient: new HttpClient(stub));

        Assert.False(await probe.IsUpdatePendingAsync());
        Assert.Empty(stub.Requests);
    }

    // ---- F-104: install churn, counted without touching the network --------

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]   // one install is an ordinary update; the version compare owns it
    [InlineData(2, true)]    // two inside the window means they are landing on top of each other
    [InlineData(5, true)]
    public void IsUpdateChurnActive_TrueOnlyWhenMoreThanOneInstallLanded(int recent, bool expected)
    {
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => null,
            handlerVersionProvider: () => null,
            recentInstallCounter: () => recent,
            httpClient: new HttpClient(new StubHttpHandler()));

        Assert.Equal(expected, probe.IsUpdateChurnActive());
    }

    [Fact]
    public void IsUpdateChurnActive_False_WhenCounterThrows()
    {
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => null,
            handlerVersionProvider: () => null,
            recentInstallCounter: () => throw new UnauthorizedAccessException("versions dir locked"),
            httpClient: new HttpClient(new StubHttpHandler()));

        Assert.False(probe.IsUpdateChurnActive());
    }

    [Fact]
    public void IsUpdateChurnActive_MakesNoNetworkCall()
    {
        // The point of the second signal: it answers with the network down, and it answers before
        // the gate is willing to spend a CDN round-trip.
        var stub = new StubHttpHandler();
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => null,
            handlerVersionProvider: () => null,
            recentInstallCounter: () => 3,
            httpClient: new HttpClient(stub));

        Assert.True(probe.IsUpdateChurnActive());
        Assert.Empty(stub.Requests);
    }

    // ---- Convenience ctor + interface --------------------------------------

    [Fact]
    public void NullLoggerConvenienceCtor_DoesNotThrow()
    {
        // The parameterless / HttpClient-only ctor wires the real seams (process scan + compat read)
        // with a NullLogger default, mirroring the other Core diagnostics. Construction must not throw.
        IRobloxUpdateProbe probe = new RobloxUpdateProbe(new HttpClient(new StubHttpHandler()));
        Assert.NotNull(probe);
    }
}
