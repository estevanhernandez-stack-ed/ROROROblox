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
            httpClient: new HttpClient(new StubHttpHandler()));

        Assert.True(probe.IsInstallerRunning());
    }

    [Fact]
    public void IsInstallerRunning_False_WhenInjectedScanReportsProcessAbsent()
    {
        var probe = new RobloxUpdateProbe(
            installerRunning: () => false,
            installedVersionProvider: () => "2.661.0.6610701",
            httpClient: new HttpClient(new StubHttpHandler()));

        Assert.False(probe.IsInstallerRunning());
    }

    [Fact]
    public void IsInstallerRunning_False_WhenScanThrows()
    {
        var probe = new RobloxUpdateProbe(
            installerRunning: () => throw new InvalidOperationException("scan blew up"),
            installedVersionProvider: () => "2.661.0.6610701",
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
            httpClient: new HttpClient(stub));

        await probe.IsUpdatePendingAsync();

        var req = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Equal(ClientVersionUrl, req.RequestUri?.ToString());
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
