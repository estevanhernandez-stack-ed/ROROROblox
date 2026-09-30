using System.IO;
using System.Security.Cryptography;
using System.Text;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Part B auto-rejoin, task 5 — <see cref="Account.AutoRejoin"/> field +
/// <see cref="IAccountStore.SetAutoRejoinAsync"/>. Mirrors
/// <see cref="AccountStoreJoinViaFriendTests"/>'s structure (defaults, round-trip through the DPAPI
/// envelope, no-op-write avoidance, unknown-id no-op, legacy-blob default).
/// </summary>
public class AccountStoreAutoRejoinTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public AccountStoreAutoRejoinTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-test-autorejoin-{Guid.NewGuid():N}");
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
    public async Task AutoRejoin_DefaultsFalse_OnAdd()
    {
        using var store = new AccountStore(_filePath);

        var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");

        Assert.False(added.AutoRejoin);
        var list = await store.ListAsync();
        Assert.False(list.Single().AutoRejoin);
    }

    [Fact]
    public async Task SetAutoRejoinAsync_FlipsAndPersists()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("U", "https://x", "c");

        await store.SetAutoRejoinAsync(added.Id, true);

        var list = await store.ListAsync();
        Assert.True(list.Single().AutoRejoin);

        await store.SetAutoRejoinAsync(added.Id, false);
        Assert.False((await store.ListAsync()).Single().AutoRejoin);
    }

    [Fact]
    public async Task SetAutoRejoinAsync_RoundTripsThroughDpapiStore()
    {
        Guid id;
        {
            using var store = new AccountStore(_filePath);
            var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");
            id = added.Id;
            await store.SetAutoRejoinAsync(id, true);
        }

        // Reopen — verify DPAPI envelope roundtrips the new property cleanly.
        using var reopened = new AccountStore(_filePath);
        var list = await reopened.ListAsync();

        Assert.Single(list);
        Assert.True(list[0].AutoRejoin);
        // DPAPI envelope still works — cookie still retrievable.
        Assert.Equal("fake-cookie", await reopened.RetrieveCookieAsync(id));
    }

    [Fact]
    public async Task SetAutoRejoinAsync_NoOpWrite_WhenUnchanged()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");
        await store.SetAutoRejoinAsync(added.Id, true);

        // Capture the file's mtime + length after the first write.
        var fileInfoAfterFirst = new FileInfo(_filePath);
        var firstWriteTime = fileInfoAfterFirst.LastWriteTimeUtc;
        var firstLength = fileInfoAfterFirst.Length;

        // Wait long enough that any second write would produce a strictly-later mtime even on
        // filesystems with second-resolution timestamps.
        await Task.Delay(1100);

        await store.SetAutoRejoinAsync(added.Id, true); // same value — should be no-op

        var fileInfoAfterSecond = new FileInfo(_filePath);
        Assert.Equal(firstWriteTime, fileInfoAfterSecond.LastWriteTimeUtc);
        Assert.Equal(firstLength, fileInfoAfterSecond.Length);

        var list = await store.ListAsync();
        Assert.True(list.Single().AutoRejoin);
    }

    [Fact]
    public async Task SetAutoRejoinAsync_UnknownId_NoOp()
    {
        using var store = new AccountStore(_filePath);
        await store.AddAsync("U", "https://x", "c");

        await store.SetAutoRejoinAsync(Guid.NewGuid(), true); // shouldn't throw

        Assert.False((await store.ListAsync()).Single().AutoRejoin);
    }

    [Fact]
    public async Task Account_LegacyDpapiBlobWithoutAutoRejoin_DefaultsFalse()
    {
        var path = Path.Combine(_tempDir, "legacy-accounts.dat");
        var legacy = """
            {
              "version": 1,
              "accounts": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "displayName": "TestUser",
                  "avatarUrl": "https://avatar/img.png",
                  "cookie": "fake-cookie",
                  "createdAt": "2026-04-01T00:00:00+00:00",
                  "lastLaunchedAt": null,
                  "isMain": true,
                  "sortOrder": 0,
                  "isSelected": true
                }
              ]
            }
            """;
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(legacy),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(path, encrypted);

        using var store = new AccountStore(path);
        var list = await store.ListAsync();

        Assert.Single(list);
        Assert.False(list[0].AutoRejoin);
        Assert.Equal("TestUser", list[0].DisplayName);
    }
}
