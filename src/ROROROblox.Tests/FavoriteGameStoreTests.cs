using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

public class FavoriteGameStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public FavoriteGameStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-favorites-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "favorites.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsEmpty_WhenFileDoesNotExist()
    {
        using var store = new FavoriteGameStore(_filePath);

        var list = await store.ListAsync();

        Assert.Empty(list);
    }

    [Fact]
    public async Task AddAsync_FirstFavorite_AutoMarksDefault()
    {
        using var store = new FavoriteGameStore(_filePath);

        var added = await store.AddAsync(920587237, 1818, "Adopt Me!", "https://x");

        Assert.True(added.IsDefault);
        Assert.Equal(920587237, added.PlaceId);
        Assert.Equal("Adopt Me!", added.Name);
    }

    [Fact]
    public async Task AddAsync_SecondFavorite_PreservesFirstAsDefault()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "First", "https://1");

        var second = await store.AddAsync(222, 2, "Second", "https://2");

        Assert.False(second.IsDefault);
        var defaults = await store.GetDefaultAsync();
        Assert.NotNull(defaults);
        Assert.Equal(111, defaults!.PlaceId);
    }

    [Fact]
    public async Task AddAsync_ExistingPlaceId_UpdatesInPlace()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "Old Name", "https://old");

        var updated = await store.AddAsync(111, 1, "New Name", "https://new");

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal("New Name", updated.Name);
        Assert.True(updated.IsDefault);
    }

    [Fact]
    public async Task SetDefaultAsync_ClearsOthersAndSetsTarget()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "First", "https://1");
        await store.AddAsync(222, 2, "Second", "https://2");

        await store.SetDefaultAsync(222);

        var defaults = await store.GetDefaultAsync();
        Assert.Equal(222, defaults!.PlaceId);
        var list = await store.ListAsync();
        Assert.Single(list, f => f.IsDefault);
    }

    [Fact]
    public async Task SetDefaultAsync_UnknownPlaceId_Throws()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "First", "https://1");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.SetDefaultAsync(999));
    }

    [Fact]
    public async Task RemoveAsync_OfDefault_PromotesNext()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "First", "https://1");
        await store.AddAsync(222, 2, "Second", "https://2");

        await store.RemoveAsync(111);

        var defaults = await store.GetDefaultAsync();
        Assert.NotNull(defaults);
        Assert.Equal(222, defaults!.PlaceId);
    }

    [Fact]
    public async Task RemoveAsync_OfLastFavorite_LeavesEmpty()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "Only", "https://1");

        await store.RemoveAsync(111);

        var list = await store.ListAsync();
        Assert.Empty(list);
        var defaults = await store.GetDefaultAsync();
        Assert.Null(defaults);
    }

    [Fact]
    public async Task ColdStart_ReadsBackPersistedFavorites()
    {
        var first = new FavoriteGameStore(_filePath);
        await first.AddAsync(111, 1, "First", "https://1");
        await first.AddAsync(222, 2, "Second", "https://2");
        await first.SetDefaultAsync(222);
        first.Dispose();

        using var second = new FavoriteGameStore(_filePath);
        var list = await second.ListAsync();
        var defaults = await second.GetDefaultAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal(222, defaults!.PlaceId);
    }

    [Fact]
    public async Task TamperedJson_ReturnsEmpty()
    {
        File.WriteAllText(_filePath, "{ this is not valid JSON");
        using var store = new FavoriteGameStore(_filePath);

        var list = await store.ListAsync();

        Assert.Empty(list);
    }

    [Fact]
    public async Task AddAsync_RejectsInvalidArgs()
    {
        using var store = new FavoriteGameStore(_filePath);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AddAsync(0, 1, "x", "y"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.AddAsync(-1, 1, "x", "y"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync(1, 1, "", "y"));
    }
}
