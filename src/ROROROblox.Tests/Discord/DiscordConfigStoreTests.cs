using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

public class DiscordConfigStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"rororo-discord-{Guid.NewGuid():N}.dat");

    public void Dispose() { if (File.Exists(_path)) File.Delete(_path); }

    [Fact]
    public async Task LoadAsync_NoFile_ReturnsDefaultsWithEverythingOff()
    {
        var store = new DiscordConfigStore(_path);

        var config = await store.LoadAsync();

        // For 806 users the safe default is silence.
        Assert.False(config.PresenceEnabled);
        Assert.False(config.JoinEnabled);
        Assert.Null(config.MineWebhookUrl);
        Assert.Equal(AlertDestination.None, config.DroppedOutDestination);
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsAcrossInstances()
    {
        var store = new DiscordConfigStore(_path);
        await store.SaveAsync(new DiscordConfig
        {
            PresenceEnabled = true,
            MineWebhookUrl = "https://discord.com/api/webhooks/1/abc",
            DroppedOutDestination = AlertDestination.Mine,
        });

        var reloaded = await new DiscordConfigStore(_path).LoadAsync();

        Assert.True(reloaded.PresenceEnabled);
        Assert.Equal("https://discord.com/api/webhooks/1/abc", reloaded.MineWebhookUrl);
        Assert.Equal(AlertDestination.Mine, reloaded.DroppedOutDestination);
    }

    [Fact]
    public async Task SavedFile_DoesNotContainTheWebhookUrlInPlaintext()
    {
        // THE test for this task. Writing the JSON unencrypted makes it fail, and that is
        // exactly what the May implementation did.
        var store = new DiscordConfigStore(_path);
        await store.SaveAsync(new DiscordConfig { MineWebhookUrl = "https://discord.com/api/webhooks/1/SECRET_TOKEN" });

        var raw = await File.ReadAllBytesAsync(_path);
        var asText = System.Text.Encoding.UTF8.GetString(raw);

        Assert.DoesNotContain("SECRET_TOKEN", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("webhooks", asText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        // A stray or wrong-user file must not break app startup. Same rule as ConsentStore.
        await File.WriteAllTextAsync(_path, "this is not a DPAPI envelope");

        var config = await new DiscordConfigStore(_path).LoadAsync();

        Assert.False(config.PresenceEnabled);
    }
}
