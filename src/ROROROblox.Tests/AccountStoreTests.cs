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

    [Fact]
    public async Task AddAsync_FirstAccount_AutoPromotesToMain()
    {
        using var store = new AccountStore(_filePath);

        var account = await store.AddAsync("FirstUser", "https://avatar/1", "cookie-1");

        Assert.True(account.IsMain);
    }

    [Fact]
    public async Task AddAsync_SubsequentAccounts_DoNotBecomeMain()
    {
        using var store = new AccountStore(_filePath);
        await store.AddAsync("FirstUser", "https://avatar/1", "cookie-1");

        var second = await store.AddAsync("SecondUser", "https://avatar/2", "cookie-2");

        Assert.False(second.IsMain);
        var list = await store.ListAsync();
        Assert.Single(list, a => a.IsMain);
    }

    [Fact]
    public async Task SetMainAsync_FlipsExactlyOneMain()
    {
        using var store = new AccountStore(_filePath);
        var first = await store.AddAsync("First", "https://x", "c1");
        var second = await store.AddAsync("Second", "https://x", "c2");
        var third = await store.AddAsync("Third", "https://x", "c3");
        Assert.True(first.IsMain);

        await store.SetMainAsync(second.Id);

        var list = await store.ListAsync();
        Assert.False(list.Single(a => a.Id == first.Id).IsMain);
        Assert.True(list.Single(a => a.Id == second.Id).IsMain);
        Assert.False(list.Single(a => a.Id == third.Id).IsMain);
    }

    [Fact]
    public async Task SetMainAsync_GuidEmpty_UnsetsAll()
    {
        using var store = new AccountStore(_filePath);
        await store.AddAsync("First", "https://x", "c1");
        await store.AddAsync("Second", "https://x", "c2");

        await store.SetMainAsync(Guid.Empty);

        var list = await store.ListAsync();
        Assert.DoesNotContain(list, a => a.IsMain);
    }

    [Fact]
    public async Task RemoveAsync_RemovingMain_AutoPromotesNextRemaining()
    {
        using var store = new AccountStore(_filePath);
        var first = await store.AddAsync("First", "https://x", "c1");   // auto-main
        var second = await store.AddAsync("Second", "https://x", "c2");
        Assert.True(first.IsMain);

        await store.RemoveAsync(first.Id);

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.True(list[0].IsMain);
        Assert.Equal(second.Id, list[0].Id);
    }

    [Fact]
    public async Task RemoveAsync_RemovingNonMain_LeavesMainUntouched()
    {
        using var store = new AccountStore(_filePath);
        var first = await store.AddAsync("First", "https://x", "c1");
        var second = await store.AddAsync("Second", "https://x", "c2");
        Assert.True(first.IsMain);

        await store.RemoveAsync(second.Id);

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.True(list[0].IsMain);
        Assert.Equal(first.Id, list[0].Id);
    }

    [Fact]
    public async Task RemoveAsync_RemovingOnlyAccount_LeavesEmptyStore()
    {
        using var store = new AccountStore(_filePath);
        var first = await store.AddAsync("Solo", "https://x", "c1");

        await store.RemoveAsync(first.Id);

        var list = await store.ListAsync();
        Assert.Empty(list);
    }

    [Fact]
    public async Task UpdateSortOrderAsync_RenumbersAccountsByGivenOrder()
    {
        using var store = new AccountStore(_filePath);
        var first = await store.AddAsync("First", "https://x", "c1");
        var second = await store.AddAsync("Second", "https://x", "c2");
        var third = await store.AddAsync("Third", "https://x", "c3");

        // Reverse: third, first, second.
        await store.UpdateSortOrderAsync(new[] { third.Id, first.Id, second.Id });

        var list = await store.ListAsync();
        Assert.Equal(0, list.Single(a => a.Id == third.Id).SortOrder);
        Assert.Equal(1, list.Single(a => a.Id == first.Id).SortOrder);
        Assert.Equal(2, list.Single(a => a.Id == second.Id).SortOrder);
    }

    [Fact]
    public async Task UpdateSortOrderAsync_PersistsAcrossInstances()
    {
        Guid firstId, secondId;
        {
            using var store = new AccountStore(_filePath);
            var first = await store.AddAsync("First", "https://x", "c1");
            var second = await store.AddAsync("Second", "https://x", "c2");
            firstId = first.Id;
            secondId = second.Id;
            await store.UpdateSortOrderAsync(new[] { secondId, firstId });
        }

        using var reopened = new AccountStore(_filePath);
        var list = await reopened.ListAsync();
        Assert.Equal(0, list.Single(a => a.Id == secondId).SortOrder);
        Assert.Equal(1, list.Single(a => a.Id == firstId).SortOrder);
    }

    [Fact]
    public async Task AddAsync_AfterReorder_NewAccountLandsAtTheEnd()
    {
        using var store = new AccountStore(_filePath);
        var a = await store.AddAsync("A", "https://x", "c1");
        var b = await store.AddAsync("B", "https://x", "c2");
        await store.UpdateSortOrderAsync(new[] { b.Id, a.Id });

        var c = await store.AddAsync("C", "https://x", "c3");

        var list = await store.ListAsync();
        Assert.Equal(0, list.Single(x => x.Id == b.Id).SortOrder);
        Assert.Equal(1, list.Single(x => x.Id == a.Id).SortOrder);
        // C should land after both reordered accounts.
        Assert.Equal(2, list.Single(x => x.Id == c.Id).SortOrder);
    }

    [Fact]
    public async Task NewAccount_DefaultsToSelectedTrue()
    {
        using var store = new AccountStore(_filePath);

        var added = await store.AddAsync("U", "https://x", "c");

        Assert.True(added.IsSelected);
        var list = await store.ListAsync();
        Assert.True(list.Single().IsSelected);
    }

    [Fact]
    public async Task SetSelectedAsync_FlipsAndPersists()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("U", "https://x", "c");

        await store.SetSelectedAsync(added.Id, false);

        var list = await store.ListAsync();
        Assert.False(list.Single().IsSelected);

        await store.SetSelectedAsync(added.Id, true);
        Assert.True((await store.ListAsync()).Single().IsSelected);
    }

    [Fact]
    public async Task SetSelectedAsync_PersistsAcrossInstances()
    {
        Guid id;
        {
            using var store = new AccountStore(_filePath);
            var added = await store.AddAsync("U", "https://x", "c");
            id = added.Id;
            await store.SetSelectedAsync(id, false);
        }

        using var reopened = new AccountStore(_filePath);
        var list = await reopened.ListAsync();
        Assert.False(list.Single(a => a.Id == id).IsSelected);
    }

    [Fact]
    public async Task SetSelectedAsync_UnknownId_NoOp()
    {
        using var store = new AccountStore(_filePath);
        await store.AddAsync("U", "https://x", "c");

        await store.SetSelectedAsync(Guid.NewGuid(), false); // shouldn't throw

        Assert.True((await store.ListAsync()).Single().IsSelected);
    }

    [Fact]
    public async Task SetMainAsync_PersistsAcrossStoreInstances()
    {
        Account second;
        {
            using var store = new AccountStore(_filePath);
            await store.AddAsync("First", "https://x", "c1");
            second = await store.AddAsync("Second", "https://x", "c2");
            await store.SetMainAsync(second.Id);
        }

        using var reopened = new AccountStore(_filePath);
        var list = await reopened.ListAsync();
        Assert.True(list.Single(a => a.Id == second.Id).IsMain);
    }

    [Fact]
    public async Task SetFpsCapAsync_RoundTrip_PersistsValue()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("alice", "https://example/a.png", "ROBLOSECURITY-stub-1");

        await store.SetFpsCapAsync(added.Id, 60);

        var listed = (await store.ListAsync()).Single();
        Assert.Equal(60, listed.FpsCap);
    }

    [Fact]
    public async Task SetFpsCapAsync_NullClearsValue()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("alice", "https://example/a.png", "ROBLOSECURITY-stub-1");
        await store.SetFpsCapAsync(added.Id, 144);

        await store.SetFpsCapAsync(added.Id, null);

        var listed = (await store.ListAsync()).Single();
        Assert.Null(listed.FpsCap);
    }

    [Fact]
    public async Task SetFpsCapAsync_OutOfRangeIsClamped()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("alice", "https://example/a.png", "ROBLOSECURITY-stub-1");

        await store.SetFpsCapAsync(added.Id, 99999);

        var listed = (await store.ListAsync()).Single();
        Assert.Equal(FpsPresets.MaxCustom, listed.FpsCap);
    }

    // ---------- v1.3.x — UpdateLocalNameAsync (DPAPI roundtrip) ----------

    [Fact]
    public async Task UpdateLocalNameAsync_HappyPath_RoundtripsThroughDpapiAcrossColdStart()
    {
        Guid id;
        {
            using var store = new AccountStore(_filePath);
            var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");
            id = added.Id;
            await store.UpdateLocalNameAsync(id, "Mr. Solo Dolo");
        }

        // Reopen — verify DPAPI envelope roundtrips the new property cleanly.
        using var reopened = new AccountStore(_filePath);
        var list = await reopened.ListAsync();

        Assert.Single(list);
        Assert.Equal("Mr. Solo Dolo", list[0].LocalName);
        Assert.Equal("TestUser", list[0].DisplayName);
        // DPAPI envelope still works — cookie still retrievable.
        Assert.Equal("fake-cookie", await reopened.RetrieveCookieAsync(id));
    }

    [Fact]
    public async Task UpdateLocalNameAsync_NullInput_ClearsLocalName()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");
        await store.UpdateLocalNameAsync(added.Id, "Custom");

        await store.UpdateLocalNameAsync(added.Id, null);

        var list = await store.ListAsync();
        Assert.Null(list[0].LocalName);
    }

    [Fact]
    public async Task UpdateLocalNameAsync_EmptyOrWhitespace_NormalizesToNull()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");
        await store.UpdateLocalNameAsync(added.Id, "Custom");

        await store.UpdateLocalNameAsync(added.Id, "  \t  ");

        var list = await store.ListAsync();
        Assert.Null(list[0].LocalName);
    }

    [Fact]
    public async Task UpdateLocalNameAsync_MissingId_ThrowsKeyNotFoundException()
    {
        using var store = new AccountStore(_filePath);
        await store.AddAsync("TestUser", "https://avatar", "fake-cookie");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => store.UpdateLocalNameAsync(Guid.NewGuid(), "Custom"));
    }

    [Fact]
    public async Task UpdateLocalNameAsync_PreservesOtherAccountFields()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");
        await store.SetMainAsync(added.Id);
        await store.SetFpsCapAsync(added.Id, 60);
        await store.SetCaptionColorAsync(added.Id, "#17d4fa");

        await store.UpdateLocalNameAsync(added.Id, "Mr. Solo Dolo");

        var list = await store.ListAsync();
        Assert.Equal("Mr. Solo Dolo", list[0].LocalName);
        Assert.True(list[0].IsMain);
        Assert.Equal(60, list[0].FpsCap);
        Assert.Equal("#17d4fa", list[0].CaptionColorHex);
    }
}
