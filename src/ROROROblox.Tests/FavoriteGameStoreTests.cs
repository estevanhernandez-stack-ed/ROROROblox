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

    // ---------- v1.3.x — UpdateLocalNameAsync + DefaultChanged + re-add preservation ----------

    [Fact]
    public async Task UpdateLocalNameAsync_HappyPath_SetsAndPersistsAcrossColdStart()
    {
        var first = new FavoriteGameStore(_filePath);
        await first.AddAsync(111, 1, "Adopt Me", "https://x");

        await first.UpdateLocalNameAsync(111, "My Adopt Me");
        first.Dispose();

        using var second = new FavoriteGameStore(_filePath);
        var list = await second.ListAsync();

        Assert.Equal("My Adopt Me", list[0].LocalName);
        Assert.Equal("Adopt Me", list[0].Name);
    }

    [Fact]
    public async Task UpdateLocalNameAsync_NullInput_ClearsLocalName()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "Adopt Me", "https://x");
        await store.UpdateLocalNameAsync(111, "Custom");

        await store.UpdateLocalNameAsync(111, null);

        var list = await store.ListAsync();
        Assert.Null(list[0].LocalName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t \n")]
    public async Task UpdateLocalNameAsync_EmptyOrWhitespace_NormalizesToNull(string input)
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "Adopt Me", "https://x");
        await store.UpdateLocalNameAsync(111, "Custom");

        await store.UpdateLocalNameAsync(111, input);

        var list = await store.ListAsync();
        Assert.Null(list[0].LocalName);
    }

    [Fact]
    public async Task UpdateLocalNameAsync_TrimsLeadingAndTrailingWhitespace()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "Adopt Me", "https://x");

        await store.UpdateLocalNameAsync(111, "   Padded Name   ");

        var list = await store.ListAsync();
        Assert.Equal("Padded Name", list[0].LocalName);
    }

    [Fact]
    public async Task UpdateLocalNameAsync_MissingPlaceId_ThrowsKeyNotFoundException()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "Adopt Me", "https://x");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => store.UpdateLocalNameAsync(999, "Custom"));
    }

    [Fact]
    public async Task SetDefaultAsync_FiresDefaultChangedOnceOnRealChange()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "First", "https://1");
        await store.AddAsync(222, 2, "Second", "https://2");

        var fireCount = 0;
        store.DefaultChanged += (_, _) => Interlocked.Increment(ref fireCount);

        await store.SetDefaultAsync(222);

        Assert.Equal(1, fireCount);
    }

    [Fact]
    public async Task SetDefaultAsync_DoesNotFireDefaultChanged_OnNoOpReSet()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "First", "https://1"); // first add becomes default

        var fireCount = 0;
        store.DefaultChanged += (_, _) => Interlocked.Increment(ref fireCount);

        await store.SetDefaultAsync(111); // already the default — no-op

        Assert.Equal(0, fireCount);
    }

    [Fact]
    public async Task AddAsync_ReAdd_PreservesLocalName()
    {
        using var store = new FavoriteGameStore(_filePath);
        await store.AddAsync(111, 1, "Original Name", "https://old");
        await store.UpdateLocalNameAsync(111, "Custom Nickname");

        // Re-add same placeId with new metadata (Name + ThumbnailUrl + UniverseId).
        var reAdded = await store.AddAsync(111, 99, "Roblox-Renamed", "https://new");

        Assert.Equal("Custom Nickname", reAdded.LocalName);
        Assert.Equal("Roblox-Renamed", reAdded.Name);
        Assert.Equal(99, reAdded.UniverseId);

        var list = await store.ListAsync();
        Assert.Equal("Custom Nickname", list[0].LocalName);
    }
}
