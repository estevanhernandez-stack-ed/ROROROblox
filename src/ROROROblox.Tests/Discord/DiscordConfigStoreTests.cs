using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.App.Discord;
using ROROROblox.Core.Discord;

namespace ROROROblox.Tests.Discord;

/// <summary>
/// Real-disk roundtrip + malformed-recovery + atomic-write + file-watcher coverage for
/// <see cref="DiscordConfigStore"/>. Each test owns its own temp directory; FileSystemWatcher
/// events fire asynchronously on the OS thread pool, so watcher tests poll within a budget
/// rather than asserting instantly.
/// </summary>
public class DiscordConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public DiscordConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-discord-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "discord-config.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void DefaultsWhenFileMissing()
    {
        using var store = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);

        Assert.False(store.RichPresenceEnabled);
        Assert.Null(store.WebhookUrl);
        Assert.Equal(DiscordWebhookEvents.AllOff, store.WebhookEvents);
    }

    [Fact]
    public void EmptyFileReadsAsDefaults()
    {
        File.WriteAllBytes(_filePath, []);

        using var store = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);

        Assert.False(store.RichPresenceEnabled);
        Assert.Null(store.WebhookUrl);
        Assert.Equal(DiscordWebhookEvents.AllOff, store.WebhookEvents);
    }

    [Fact]
    public async Task SaveAsync_RoundtripsThroughFreshStore()
    {
        var snapshot = new DiscordConfigSnapshot(
            RichPresenceEnabled: true,
            WebhookUrl: "https://discord.com/api/webhooks/123/abc",
            WebhookEvents: new DiscordWebhookEvents(true, false, true));

        using (var writer = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance))
        {
            await writer.SaveAsync(snapshot, CancellationToken.None);
        }

        using var reader = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);
        Assert.True(reader.RichPresenceEnabled);
        Assert.Equal("https://discord.com/api/webhooks/123/abc", reader.WebhookUrl);
        Assert.True(reader.WebhookEvents.OnLaunch);
        Assert.False(reader.WebhookEvents.OnPrivateServerJoin);
        Assert.True(reader.WebhookEvents.OnNAccountsActive);
    }

    [Fact]
    public async Task SaveAsync_SnapshotWithNullWebhookUrl_RoundtripsCleanly()
    {
        var snapshot = new DiscordConfigSnapshot(
            RichPresenceEnabled: true,
            WebhookUrl: null,
            WebhookEvents: DiscordWebhookEvents.AllOff);

        using (var writer = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance))
        {
            await writer.SaveAsync(snapshot, CancellationToken.None);
        }

        using var reader = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);
        Assert.True(reader.RichPresenceEnabled);
        Assert.Null(reader.WebhookUrl);
    }

    [Fact]
    public async Task SaveAsync_OverwritesExistingFile_NoTempLeak()
    {
        var first = new DiscordConfigSnapshot(true, "https://discord.com/api/webhooks/1/a", DiscordWebhookEvents.AllOff);
        var second = new DiscordConfigSnapshot(false, null, DiscordWebhookEvents.AllOff);

        using (var store = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance))
        {
            await store.SaveAsync(first, CancellationToken.None);
            await store.SaveAsync(second, CancellationToken.None);
        }

        // Final state == second snapshot, .tmp gone, no .corrupt sibling.
        Assert.True(File.Exists(_filePath));
        Assert.False(File.Exists(_filePath + ".tmp"));

        var corruptSiblings = Directory.GetFiles(_tempDir, "discord-config.json.corrupt-*");
        Assert.Empty(corruptSiblings);

        using var reader = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);
        Assert.False(reader.RichPresenceEnabled);
    }

    [Fact]
    public void MalformedJson_ReturnsDefaults_PreservesCorruptFile()
    {
        File.WriteAllText(_filePath, "{ this is not json", Encoding.UTF8);

        using var store = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);

        Assert.False(store.RichPresenceEnabled);
        Assert.Null(store.WebhookUrl);
        Assert.Equal(DiscordWebhookEvents.AllOff, store.WebhookEvents);

        var corruptCopies = Directory.GetFiles(_tempDir, "discord-config.json.corrupt-*");
        Assert.Single(corruptCopies);

        var preserved = File.ReadAllText(corruptCopies[0]);
        Assert.Equal("{ this is not json", preserved);
    }

    [Fact]
    public void MalformedJson_OriginalFileMovedAside_NotLeftOnDisk()
    {
        File.WriteAllText(_filePath, "garbage{}{", Encoding.UTF8);

        using var store = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);
        _ = store.RichPresenceEnabled; // force load

        // Original path no longer holds garbage — it was renamed to .corrupt-{stamp}.
        // (Or, equivalently, doesn't contain the garbage payload anymore.)
        if (File.Exists(_filePath))
        {
            var contents = File.ReadAllText(_filePath);
            Assert.DoesNotContain("garbage", contents);
        }
    }

    [Fact]
    public async Task SaveAsync_OrphanTempFileFromPriorCrash_DoesNotPreventNewWrite()
    {
        // Simulate a prior run that crashed mid-save: a stale .tmp file with non-conforming bytes.
        File.WriteAllText(_filePath + ".tmp", "stale temp content from a crashed save");

        var snapshot = new DiscordConfigSnapshot(true, null, DiscordWebhookEvents.AllOff);

        using (var store = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance))
        {
            await store.SaveAsync(snapshot, CancellationToken.None);
        }

        Assert.True(File.Exists(_filePath));
        // After a successful save, the temp file is consumed (renamed to dest), so it must be gone.
        Assert.False(File.Exists(_filePath + ".tmp"));

        using var reader = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);
        Assert.True(reader.RichPresenceEnabled);
    }

    [Fact]
    public async Task ChangedEvent_FiresOnSaveAsync()
    {
        var fired = 0;
        using var store = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);
        store.Changed += (_, _) => Interlocked.Increment(ref fired);

        await store.SaveAsync(new DiscordConfigSnapshot(true, null, DiscordWebhookEvents.AllOff), CancellationToken.None);

        // SaveAsync raises Changed inline. Watcher may also fire a second event on the file write —
        // we just assert "at least one" so the test isn't flaky on slow CI.
        Assert.True(fired >= 1, $"Expected Changed to fire, got {fired}.");
    }

    [Fact]
    public async Task ChangedEvent_FiresOnExternalWrite()
    {
        using var store = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);

        using var fired = new ManualResetEventSlim(false);
        store.Changed += (_, _) => fired.Set();

        // External write to the watched path. JSON valid so the reload succeeds and Changed
        // semantically reflects "new content available."
        var snapshot = new DiscordConfigSnapshot(true, "https://discord.com/api/webhooks/9/z", DiscordWebhookEvents.AllOff);
        await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        }));

        // FileSystemWatcher fires async on the OS thread pool. Budget generous so flaky disks
        // don't fail the test, but bounded so a real regression surfaces.
        var got = fired.Wait(TimeSpan.FromSeconds(5));
        Assert.True(got, "Changed event did not fire within 5s of an external write.");

        // After the event fires, the cached state should reflect the new content. Property
        // reads happen off the watcher thread, so we poll briefly to allow OnFileChanged to
        // finish updating _current.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < deadline && !store.RichPresenceEnabled)
        {
            await Task.Delay(50);
        }
        Assert.True(store.RichPresenceEnabled);
        Assert.Equal("https://discord.com/api/webhooks/9/z", store.WebhookUrl);
    }

    [Fact]
    public void Constructor_RejectsEmptyPath()
    {
        Assert.Throws<ArgumentException>(() =>
            new DiscordConfigStore("", NullLogger<DiscordConfigStore>.Instance));
    }

    [Fact]
    public async Task SaveAsync_NullSnapshot_Throws()
    {
        using var store = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            store.SaveAsync(null!, CancellationToken.None));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var store = new DiscordConfigStore(_filePath, NullLogger<DiscordConfigStore>.Instance);
        store.Dispose();
        store.Dispose(); // second call must not throw
    }
}
