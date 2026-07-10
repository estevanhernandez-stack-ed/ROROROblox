using ROROROblox.Core.StreamerMode;

namespace ROROROblox.Tests;

public class FileStreamerIdentityStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"streamer-{Guid.NewGuid():N}.dat");

    [Fact]
    public async Task SaveThenLoad_RoundTripsIdentity()
    {
        var store = new FileStreamerIdentityStore(_path);
        await store.SaveAsync("friend:12345", new StreamerIdentity("CaptainNoodle", "noodle"));

        var loaded = await new FileStreamerIdentityStore(_path).LoadAllAsync();

        Assert.True(loaded.TryGetValue("friend:12345", out var id));
        Assert.Equal("CaptainNoodle", id.FakeName);
        Assert.Equal("noodle", id.FakeAvatarId);
    }

    [Fact]
    public async Task LoadAll_MissingFile_ReturnsEmpty()
        => Assert.Empty(await new FileStreamerIdentityStore(_path).LoadAllAsync());

    [Fact]
    public async Task Save_OverwritesSameKey()
    {
        var store = new FileStreamerIdentityStore(_path);
        await store.SaveAsync("friend:1", new StreamerIdentity("A", "duck"));
        await store.SaveAsync("friend:1", new StreamerIdentity("B", "potato"));

        var loaded = await store.LoadAllAsync();
        Assert.Single(loaded);
        Assert.Equal("B", loaded["friend:1"].FakeName);
    }

    public void Dispose() { try { File.Delete(_path); } catch { } }
}
