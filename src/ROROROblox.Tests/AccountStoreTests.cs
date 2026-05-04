using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Real-DPAPI roundtrip + tampered-file recovery + on-disk envelope shape verification.
/// Each test owns a unique temp directory so concurrent test runs don't collide.
/// </summary>
public class AccountStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public AccountStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "accounts.dat");
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
        using var store = new AccountStore(_filePath);

        var list = await store.ListAsync();

        Assert.Empty(list);
    }

    [Fact]
    public async Task AddAsync_ReturnsAccount_WithProvidedFields()
    {
        using var store = new AccountStore(_filePath);

        var account = await store.AddAsync("TestUser", "https://avatar.example/img.png", "fake-cookie-1");

        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("TestUser", account.DisplayName);
        Assert.Equal("https://avatar.example/img.png", account.AvatarUrl);
        Assert.Null(account.LastLaunchedAt);
        Assert.True(account.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task AddAsync_DoesNotExposeCookie_OnReturnedRecord()
    {
        using var store = new AccountStore(_filePath);

        var account = await store.AddAsync("TestUser", "https://x", "secret-cookie");

        var properties = typeof(Account).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Contains("cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddThenList_RoundTripsTheAccount()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");

        var list = await store.ListAsync();

        Assert.Single(list);
        Assert.Equal(added.Id, list[0].Id);
        Assert.Equal("TestUser", list[0].DisplayName);
    }

    [Fact]
    public async Task RetrieveCookieAsync_ReturnsTheStoredCookie()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://avatar", "the-cookie-value");

        var cookie = await store.RetrieveCookieAsync(added.Id);

        Assert.Equal("the-cookie-value", cookie);
    }

    [Fact]
    public async Task RetrieveCookieAsync_ThrowsKeyNotFound_ForUnknownId()
    {
        using var store = new AccountStore(_filePath);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.RetrieveCookieAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RemoveAsync_DropsTheAccount()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://x", "c");

        await store.RemoveAsync(added.Id);

        var list = await store.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task UpdateCookieAsync_ReplacesTheStoredCookie()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://x", "old-cookie");

        await store.UpdateCookieAsync(added.Id, "new-cookie");

        var cookie = await store.RetrieveCookieAsync(added.Id);
        Assert.Equal("new-cookie", cookie);
    }

    [Fact]
    public async Task TouchLastLaunchedAsync_StampsTheLastLaunchedField()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://x", "c");

        await store.TouchLastLaunchedAsync(added.Id);

        var list = await store.ListAsync();
        Assert.NotNull(list[0].LastLaunchedAt);
        Assert.True(list[0].LastLaunchedAt > DateTimeOffset.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task ColdStart_ReadsBackPersistedAccounts()
    {
        var firstStore = new AccountStore(_filePath);
        var added = await firstStore.AddAsync("TestUser", "https://x", "the-cookie");
        firstStore.Dispose();

        using var secondStore = new AccountStore(_filePath);
        var list = await secondStore.ListAsync();
        var cookie = await secondStore.RetrieveCookieAsync(added.Id);

        Assert.Single(list);
        Assert.Equal(added.Id, list[0].Id);
        Assert.Equal("the-cookie", cookie);
    }

    [Fact]
    public async Task TamperedFile_ThrowsAccountStoreCorruptException()
    {
        File.WriteAllBytes(_filePath, "this is not a valid DPAPI envelope, just garbage"u8.ToArray());
        using var store = new AccountStore(_filePath);

        await Assert.ThrowsAsync<AccountStoreCorruptException>(() => store.ListAsync());
    }

    [Fact]
    public async Task FileOnDisk_IsNotHumanReadable_AfterAdd()
    {
        using var store = new AccountStore(_filePath);
        await store.AddAsync("TestUser", "https://avatar", "this-is-a-distinctive-cookie-value");

        var bytes = await File.ReadAllBytesAsync(_filePath);
        var asText = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("this-is-a-distinctive-cookie-value", asText);
        Assert.DoesNotContain("TestUser", asText);
        Assert.DoesNotContain("\"displayName\"", asText);
    }

    [Fact]
    public async Task SaveAsync_RemovesTempFile_OnSuccess()
    {
        using var store = new AccountStore(_filePath);
        await store.AddAsync("TestUser", "https://x", "c");

        var tempPath = _filePath + ".tmp";
        Assert.False(File.Exists(tempPath), "Temp file should be moved into place, not left behind.");
        Assert.True(File.Exists(_filePath));
    }

    [Fact]
    public async Task AddAsync_RejectsEmptyDisplayName()
    {
        using var store = new AccountStore(_filePath);

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync("", "https://x", "c"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync("   ", "https://x", "c"));
    }

    [Fact]
    public async Task AddAsync_RejectsEmptyCookie()
    {
        using var store = new AccountStore(_filePath);

        await Assert.ThrowsAsync<ArgumentException>(() => store.AddAsync("TestUser", "https://x", ""));
    }

    [Fact]
    public async Task UpdateCookieAsync_ThrowsKeyNotFound_ForUnknownId()
    {
        using var store = new AccountStore(_filePath);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.UpdateCookieAsync(Guid.NewGuid(), "new-c"));
    }
}
