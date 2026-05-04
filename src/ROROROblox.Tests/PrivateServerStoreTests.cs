using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class PrivateServerStoreTests : IDisposable
{
    private readonly string _tempPath;

    public PrivateServerStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"rororoblox-test-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try { if (File.Exists(_tempPath)) File.Delete(_tempPath); }
        catch { }
    }

    [Fact]
    public async Task ListAsync_OnFreshFile_ReturnsEmpty()
    {
        using var store = new PrivateServerStore(_tempPath);
        var list = await store.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task AddAsync_PersistsAndReturnsRecord()
    {
        using var store = new PrivateServerStore(_tempPath);
        var added = await store.AddAsync(
            placeId: 920587237,
            code: "share-code-1",
            codeKind: PrivateServerCodeKind.LinkCode,
            name: "Squad VIP",
            placeName: "Adopt Me",
            thumbnailUrl: "https://x/icon.png");

        Assert.Equal(920587237L, added.PlaceId);
        Assert.Equal("share-code-1", added.Code);
        Assert.Equal(PrivateServerCodeKind.LinkCode, added.CodeKind);
        Assert.Equal("Squad VIP", added.Name);
        Assert.NotEqual(Guid.Empty, added.Id);
        Assert.True(added.AddedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Null(added.LastLaunchedAt);

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal(added, list[0]);
    }

    [Fact]
    public async Task AddAsync_SamePlaceAndCode_ReplacesPreservingIdAndAddedAt()
    {
        using var store = new PrivateServerStore(_tempPath);
        var first = await store.AddAsync(100, "code", PrivateServerCodeKind.LinkCode, "Original Name", "Place", "");

        // Force AddedAt drift detection by waiting a tick.
        await Task.Delay(10);

        var second = await store.AddAsync(100, "code", PrivateServerCodeKind.LinkCode, "Renamed", "Place", "");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.AddedAt, second.AddedAt);
        Assert.Equal("Renamed", second.Name);

        var list = await store.ListAsync();
        Assert.Single(list);
    }

    [Fact]
    public async Task AddAsync_DifferentCodeSamePlace_AddsSeparate()
    {
        using var store = new PrivateServerStore(_tempPath);
        await store.AddAsync(100, "code-a", PrivateServerCodeKind.LinkCode, "Server A", "Place", "");
        await store.AddAsync(100, "code-b", PrivateServerCodeKind.LinkCode, "Server B", "Place", "");

        var list = await store.ListAsync();
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task AddAsync_PreservesCodeKindThroughRoundTrip()
    {
        using var store = new PrivateServerStore(_tempPath);
        await store.AddAsync(1, "share", PrivateServerCodeKind.LinkCode, "Share", "p", "");
        await store.AddAsync(2, "raw", PrivateServerCodeKind.AccessCode, "Raw", "p", "");

        var list = await store.ListAsync();
        Assert.Equal(PrivateServerCodeKind.LinkCode, list.Single(s => s.Code == "share").CodeKind);
        Assert.Equal(PrivateServerCodeKind.AccessCode, list.Single(s => s.Code == "raw").CodeKind);
    }

    [Fact]
    public async Task GetAsync_KnownId_ReturnsRecord()
    {
        using var store = new PrivateServerStore(_tempPath);
        var added = await store.AddAsync(100, "c", PrivateServerCodeKind.LinkCode, "n", "p", "");
        var fetched = await store.GetAsync(added.Id);
        Assert.Equal(added, fetched);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        using var store = new PrivateServerStore(_tempPath);
        Assert.Null(await store.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoveAsync_KnownId_RemovesRecord()
    {
        using var store = new PrivateServerStore(_tempPath);
        var a = await store.AddAsync(1, "a", PrivateServerCodeKind.LinkCode, "A", "p", "");
        var b = await store.AddAsync(2, "b", PrivateServerCodeKind.LinkCode, "B", "p", "");

        await store.RemoveAsync(a.Id);

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal(b.Id, list[0].Id);
    }

    [Fact]
    public async Task RemoveAsync_UnknownId_NoOp()
    {
        using var store = new PrivateServerStore(_tempPath);
        await store.AddAsync(1, "a", PrivateServerCodeKind.LinkCode, "A", "p", "");
        await store.RemoveAsync(Guid.NewGuid());
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task TouchLastLaunchedAsync_StampsTimestamp()
    {
        using var store = new PrivateServerStore(_tempPath);
        var added = await store.AddAsync(1, "a", PrivateServerCodeKind.LinkCode, "A", "p", "");
        Assert.Null(added.LastLaunchedAt);

        await store.TouchLastLaunchedAsync(added.Id);

        var fetched = await store.GetAsync(added.Id);
        Assert.NotNull(fetched);
        Assert.NotNull(fetched!.LastLaunchedAt);
        Assert.True(fetched.LastLaunchedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task TouchLastLaunchedAsync_UnknownId_NoOp()
    {
        using var store = new PrivateServerStore(_tempPath);
        await store.AddAsync(1, "a", PrivateServerCodeKind.LinkCode, "A", "p", "");
        await store.TouchLastLaunchedAsync(Guid.NewGuid()); // shouldn't throw
        var list = await store.ListAsync();
        Assert.Null(list[0].LastLaunchedAt);
    }

    [Fact]
    public async Task PersistsAcrossInstances()
    {
        using (var firstStore = new PrivateServerStore(_tempPath))
        {
            await firstStore.AddAsync(42, "code", PrivateServerCodeKind.LinkCode, "Persisted", "Place", "");
        }

        using var secondStore = new PrivateServerStore(_tempPath);
        var list = await secondStore.ListAsync();
        Assert.Single(list);
        Assert.Equal("Persisted", list[0].Name);
        Assert.Equal(PrivateServerCodeKind.LinkCode, list[0].CodeKind);
    }

    [Fact]
    public async Task LoadAsync_LegacyFileWithAccessCodeOnly_HydratesAsLinkCode()
    {
        // Older builds (pre-discriminator) wrote private-servers.json with accessCode only.
        // The on-disk format used PascalCase from the record. Expectation: legacy values rehydrate
        // as LinkCode (the share-URL form, which is what users actually pasted under the buggy
        // pre-discriminator code).
        Directory.CreateDirectory(Path.GetDirectoryName(_tempPath)!);
        var legacyJson = """
        {
          "version": 1,
          "servers": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "placeId": 920587237,
              "accessCode": "legacy-share-code",
              "name": "Old Squad",
              "placeName": "Adopt Me",
              "thumbnailUrl": "",
              "addedAt": "2026-01-01T00:00:00+00:00",
              "lastLaunchedAt": null
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(_tempPath, legacyJson);

        using var store = new PrivateServerStore(_tempPath);
        var list = await store.ListAsync();

        Assert.Single(list);
        Assert.Equal(920587237L, list[0].PlaceId);
        Assert.Equal("legacy-share-code", list[0].Code);
        Assert.Equal(PrivateServerCodeKind.LinkCode, list[0].CodeKind);
        Assert.Equal("Old Squad", list[0].Name);
    }

    [Fact]
    public async Task LoadAsync_CorruptFile_FallsBackToEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_tempPath)!);
        await File.WriteAllTextAsync(_tempPath, "{ this is not valid json");

        using var store = new PrivateServerStore(_tempPath);
        var list = await store.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task AddAsync_RejectsInvalidInput()
    {
        using var store = new PrivateServerStore(_tempPath);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            store.AddAsync(0, "code", PrivateServerCodeKind.LinkCode, "name", "place", ""));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddAsync(1, "", PrivateServerCodeKind.LinkCode, "name", "place", ""));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.AddAsync(1, "code", PrivateServerCodeKind.LinkCode, "", "place", ""));
    }
}
