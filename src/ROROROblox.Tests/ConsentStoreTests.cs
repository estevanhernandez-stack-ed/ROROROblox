using System.IO;
using ROROROblox.App.Plugins;

namespace ROROROblox.Tests;

public class ConsentStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public ConsentStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-consent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "consent.dat");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenStoreDoesNotExist()
    {
        var store = new ConsentStore(_filePath);
        var list = await store.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task GrantAsync_PersistsRecord_AndIsReadableOnRoundtrip()
    {
        var store = new ConsentStore(_filePath);
        await store.GrantAsync("626labs.test", new[] { "host.events.account-launched", "host.ui.tray-menu" });

        // New store instance to verify on-disk persistence.
        var store2 = new ConsentStore(_filePath);
        var list = await store2.ListAsync();

        var record = Assert.Single(list);
        Assert.Equal("626labs.test", record.PluginId);
        Assert.Contains("host.events.account-launched", record.GrantedCapabilities);
        Assert.False(record.AutostartEnabled); // default off
    }

    [Fact]
    public async Task SetAutostartAsync_PersistsToggle()
    {
        var store = new ConsentStore(_filePath);
        await store.GrantAsync("626labs.test", new[] { "host.events.account-launched" });
        await store.SetAutostartAsync("626labs.test", enabled: true);

        var store2 = new ConsentStore(_filePath);
        var list = await store2.ListAsync();
        Assert.True(Assert.Single(list).AutostartEnabled);
    }

    [Fact]
    public async Task RevokeAsync_RemovesPlugin()
    {
        var store = new ConsentStore(_filePath);
        await store.GrantAsync("626labs.test", new[] { "host.events.account-launched" });
        await store.RevokeAsync("626labs.test");

        var store2 = new ConsentStore(_filePath);
        Assert.Empty(await store2.ListAsync());
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenFileIsTampered()
    {
        var store = new ConsentStore(_filePath);
        await store.GrantAsync("626labs.test", new[] { "host.events.account-launched" });

        // Tamper.
        await File.WriteAllBytesAsync(_filePath, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

        var store2 = new ConsentStore(_filePath);
        Assert.Empty(await store2.ListAsync());
    }
}
