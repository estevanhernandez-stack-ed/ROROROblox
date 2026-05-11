using System.IO;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class PluginRegistryTests : IDisposable
{
    private readonly string _pluginsRoot;
    private readonly string _consentPath;

    public PluginRegistryTests()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), $"ROROROblox-reg-{Guid.NewGuid():N}");
        _pluginsRoot = Path.Combine(tempBase, "plugins");
        _consentPath = Path.Combine(tempBase, "consent.dat");
        Directory.CreateDirectory(_pluginsRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(_pluginsRoot)!))
        {
            Directory.Delete(Path.GetDirectoryName(_pluginsRoot)!, recursive: true);
        }
    }

    private void WritePlugin(string id, string manifestJson)
    {
        var dir = Path.Combine(_pluginsRoot, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), manifestJson);
    }

    [Fact]
    public async Task ScanAsync_ReturnsEmpty_WhenNoPluginsInstalled()
    {
        var registry = new PluginRegistry(_pluginsRoot, new ConsentStore(_consentPath));
        var plugins = await registry.ScanAsync();
        Assert.Empty(plugins);
    }

    [Fact]
    public async Task ScanAsync_ReturnsManifest_WhenManifestPresent()
    {
        WritePlugin("626labs.test", """
        {"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":["host.events.account-launched"]}
        """);

        var registry = new PluginRegistry(_pluginsRoot, new ConsentStore(_consentPath));
        var plugins = await registry.ScanAsync();

        var plugin = Assert.Single(plugins);
        Assert.Equal("626labs.test", plugin.Manifest.Id);
        Assert.Equal(Path.Combine(_pluginsRoot, "626labs.test"), plugin.InstallDir);
        Assert.Empty(plugin.Consent.GrantedCapabilities); // no consent yet
    }

    [Fact]
    public async Task ScanAsync_SkipsDirectoriesWithMalformedManifest()
    {
        WritePlugin("626labs.good", """
        {"schemaVersion":1,"id":"626labs.good","name":"x","version":"1","contractVersion":"1.0","publisher":"x","description":"x","capabilities":[]}
        """);
        WritePlugin("626labs.bad", "{not json}");

        var registry = new PluginRegistry(_pluginsRoot, new ConsentStore(_consentPath));
        var plugins = await registry.ScanAsync();

        Assert.Single(plugins);
        Assert.Equal("626labs.good", plugins[0].Manifest.Id);
    }

    [Fact]
    public async Task ScanAsync_PairsManifestWithConsentRecord()
    {
        WritePlugin("626labs.test", """
        {"schemaVersion":1,"id":"626labs.test","name":"Test","version":"0.1.0","contractVersion":"1.0","publisher":"626 Labs","description":"x","capabilities":["host.ui.tray-menu"]}
        """);
        var consent = new ConsentStore(_consentPath);
        await consent.GrantAsync("626labs.test", new[] { "host.ui.tray-menu" });

        var registry = new PluginRegistry(_pluginsRoot, consent);
        var plugins = await registry.ScanAsync();

        Assert.Contains("host.ui.tray-menu", Assert.Single(plugins).Consent.GrantedCapabilities);
    }
}
