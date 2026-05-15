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

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask);
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

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask);

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

        var installer = new PluginInstaller(new HttpClient(_http), _pluginsRoot, (_, _) => Task.CompletedTask);

        await Assert.ThrowsAsync<PluginInstallerException>(() => installer.InstallAsync(
            "https://example.com/plugin/v1/",
            requireCapabilities: new[] { "host.events.account-launched" }));
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
        });

        await installer.InstallAsync("https://example.com/plugin/v1/", Array.Empty<string>());

        Assert.Equal("626labs.test", stoppedId);                            // stop hook got the manifest id
        Assert.True(oldInstallStillPresentAtStopTime);                      // ...before the destructive delete
        Assert.False(File.Exists(Path.Combine(installDir, "OLD.txt")));     // ...and the delete still happened
        Assert.True(File.Exists(Path.Combine(installDir, "manifest.json"))); // ...and fresh content extracted
    }
}
