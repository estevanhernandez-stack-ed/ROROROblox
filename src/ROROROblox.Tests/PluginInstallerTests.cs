using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class PluginInstallerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _pluginsRoot;
    private readonly StubHttpHandler _http;

    public PluginInstallerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"ROROROblox-install-{Guid.NewGuid():N}");
        _pluginsRoot = Path.Combine(_tempRoot, "plugins");
        Directory.CreateDirectory(_pluginsRoot);
        _http = new StubHttpHandler();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
    }

    private static (byte[] zipBytes, string sha256) BuildZipWithManifest(string manifestJson)
    {
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.json");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(manifestJson);
        }
        var bytes = ms.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return (bytes, sha);
    }

    [Fact]
    public async Task InstallAsync_ValidPackage_ExtractsManifestAndZipContent()
    {
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":["host.events.account-launched"]}""";
        var (zipBytes, sha) = BuildZipWithManifest(manifestJson);

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(sha) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 4, 3, 0));
        var result = await installer.InstallAsync(
            "https://example.com/plugin/v1/",
            requireCapabilities: new[] { "host.events.account-launched" });

        Assert.Equal("626labs.test", result.Manifest.Id);
        Assert.True(File.Exists(Path.Combine(_pluginsRoot, "626labs.test", "manifest.json")));
    }

    [Fact]
    public async Task InstallAsync_ShaMismatch_RefusesAndCleansUp()
    {
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[]}""";
        var (zipBytes, _) = BuildZipWithManifest(manifestJson);

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("0000000000000000000000000000000000000000000000000000000000000000") });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 4, 3, 0));

        await Assert.ThrowsAsync<PluginInstallerException>(() => installer.InstallAsync(
            "https://example.com/plugin/v1/",
            requireCapabilities: Array.Empty<string>()));

        Assert.False(Directory.Exists(Path.Combine(_pluginsRoot, "626labs.test")));
    }

    [Fact]
    public async Task InstallAsync_ManifestMissingRequiredCapability_Throws()
    {
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[]}""";
        var (zipBytes, sha) = BuildZipWithManifest(manifestJson);

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(sha) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 4, 3, 0));

        await Assert.ThrowsAsync<PluginInstallerException>(() => installer.InstallAsync(
            "https://example.com/plugin/v1/",
            requireCapabilities: new[] { "host.events.account-launched" }));
    }

    [Fact]
    public async Task InstallAsync_AutostartDefaultOn_TurnsOnInitialConsent()
    {
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[],"autostartDefault":"on"}""";
        var (zipBytes, sha) = BuildZipWithManifest(manifestJson);

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(sha) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 4, 3, 0));
        var result = await installer.InstallAsync("https://example.com/plugin/v1/", Array.Empty<string>());

        Assert.True(result.Consent.AutostartEnabled);
    }

    [Fact]
    public async Task InstallAsync_MinHostVersionAboveCurrent_RefusesWithUpdateMessage()
    {
        // The user is on 1.4.2.0 and tries to install a plugin that says "I need 1.4.3+".
        // Bail before downloading the zip — the user gets an actionable message instead
        // of a downstream gRPC failure once the plugin tries to call a host method that
        // doesn't exist yet.
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[],"minHostVersion":"1.4.3"}""";

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 4, 2, 0));

        var ex = await Assert.ThrowsAsync<PluginInstallerException>(() => installer.InstallAsync(
            "https://example.com/plugin/v1/", Array.Empty<string>()));
        Assert.Contains("1.4.3", ex.Message);
        Assert.Contains("Update RoRoRo", ex.Message);
    }

    [Fact]
    public async Task InstallAsync_MinHostVersionAtOrBelowCurrent_Succeeds()
    {
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[],"minHostVersion":"1.4.0"}""";
        var (zipBytes, sha) = BuildZipWithManifest(manifestJson);

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(sha) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 4, 3, 0));
        var result = await installer.InstallAsync("https://example.com/plugin/v1/", Array.Empty<string>());

        Assert.Equal("626labs.test", result.Manifest.Id);
    }

    [Fact]
    public async Task InstallAsync_MinHostVersionWithPrereleaseTag_ParsesNumericHead()
    {
        // Author writes "1.4.3-beta" in the manifest — semver-style pre-release suffix.
        // Treat it as 1.4.3 for the gate check; the suffix is informational, not
        // a separate ordering axis here. A host on exactly 1.4.3.0 should accept.
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[],"minHostVersion":"1.4.3-beta"}""";
        var (zipBytes, sha) = BuildZipWithManifest(manifestJson);

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(sha) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 4, 3, 0));
        var result = await installer.InstallAsync("https://example.com/plugin/v1/", Array.Empty<string>());

        Assert.Equal("626labs.test", result.Manifest.Id);
    }

    [Fact]
    public async Task InstallAsync_EntrypointNotPresentInZip_RefusesAndCleansUp()
    {
        // Plugin author says the EXE is "wrong-name.exe" but the zip only contains
        // "manifest.json". Refuse the install rather than leave the user with a row
        // that fails on Launch with an opaque error.
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[],"entrypoint":"wrong-name.exe"}""";
        var (zipBytes, sha) = BuildZipWithManifest(manifestJson);

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(sha) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 4, 3, 0));

        var ex = await Assert.ThrowsAsync<PluginInstallerException>(() => installer.InstallAsync(
            "https://example.com/plugin/v1/", Array.Empty<string>()));
        Assert.Contains("wrong-name.exe", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(_pluginsRoot, "626labs.test")));
    }

    [Fact]
    public async Task InstallAsync_EntrypointPresentInZip_ExecutablePathHonorsIt()
    {
        // Zip ships an EXE named differently from the manifest id. With entrypoint
        // declared, ExecutablePath resolves to <installDir>/<entrypoint> instead of
        // <installDir>/<id>.exe.
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[],"entrypoint":"custom-launcher.exe"}""";

        // Build a zip that contains both manifest.json AND custom-launcher.exe (placeholder bytes).
        using var ms = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var w = new StreamWriter(manifestEntry.Open(), Encoding.UTF8)) w.Write(manifestJson);
            var exeEntry = archive.CreateEntry("custom-launcher.exe");
            using (var s = exeEntry.Open()) s.Write(new byte[] { 0x4D, 0x5A }, 0, 2); // "MZ" so it looks vaguely PE-ish
        }
        var zipBytes = ms.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(sha) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask, new Version(1, 4, 3, 0));
        var result = await installer.InstallAsync("https://example.com/plugin/v1/", Array.Empty<string>());

        Assert.EndsWith("custom-launcher.exe", result.ExecutablePath);
        Assert.True(File.Exists(result.ExecutablePath));
    }

    [Fact]
    public async Task InstallAsync_StopsRunningPluginBeforeTouchingInstallDir()
    {
        // Regression guard: a running plugin process holds its own DLLs locked, so the
        // installer's Directory.Delete / extract over an existing install dir fails with
        // "access denied". The installer must stop the running instance first — mirrors
        // the stop-before-delete dance in PluginsViewModel.RevokeAsync.
        const string manifestJson = """{"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.2.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":[]}""";
        var (zipBytes, sha) = BuildZipWithManifest(manifestJson);

        // Simulate a prior install already on disk.
        var installDir = Path.Combine(_pluginsRoot, "626labs.test");
        Directory.CreateDirectory(installDir);
        File.WriteAllText(Path.Combine(installDir, "OLD.txt"), "stale");

        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(manifestJson) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(sha) });
        _http.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(zipBytes) });

        string? stoppedId = null;
        bool oldInstallStillPresentAtStopTime = false;
        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (pluginId, dir) =>
        {
            stoppedId = pluginId;
            // The stop hook must run BEFORE the installer wipes the old install dir.
            oldInstallStillPresentAtStopTime = File.Exists(Path.Combine(installDir, "OLD.txt"));
            return Task.CompletedTask;
        }, new Version(1, 4, 3, 0));

        await installer.InstallAsync("https://example.com/plugin/v1/", Array.Empty<string>());

        Assert.Equal("626labs.test", stoppedId);                            // stop hook got the manifest id
        Assert.True(oldInstallStillPresentAtStopTime);                      // ...before the destructive delete
        Assert.False(File.Exists(Path.Combine(installDir, "OLD.txt")));     // ...and the delete still happened
        Assert.True(File.Exists(Path.Combine(installDir, "manifest.json"))); // ...and fresh content extracted
    }
}
