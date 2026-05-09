using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Cycle-5 contract tests for <see cref="IAccountStore.UpdateRobloxUserIdAsync"/>. Verifies:
/// (1) the field gets set on a previously-null account,
/// (2) idempotence — calling twice with the same value doesn't write again,
/// (3) KeyNotFoundException on a missing accountId.
/// Spec §4.2, §7.
/// </summary>
public class AccountStoreUpdateRobloxUserIdTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public AccountStoreUpdateRobloxUserIdTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-test-update-userid-{Guid.NewGuid():N}");
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
    public async Task UpdateRobloxUserIdAsync_SetsField_OnPreviouslyNullAccount()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");
        Assert.Null(added.RobloxUserId);

        await store.UpdateRobloxUserIdAsync(added.Id, 9876543210L);

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal(9876543210L, list[0].RobloxUserId);
    }

    [Fact]
    public async Task UpdateRobloxUserIdAsync_IsIdempotent_WhenSameValueAlreadySet()
    {
        // Idempotence is load-bearing for the eager backfill — the orchestrator calls this
        // unconditionally for each missing account, but if a previous backfill already
        // persisted the value, we must NOT trigger a redundant DPAPI round-trip + disk write.
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");
        await store.UpdateRobloxUserIdAsync(added.Id, 42L);

        // Capture the file's mtime + length after the first write.
        var fileInfoAfterFirst = new FileInfo(_filePath);
        var firstWriteTime = fileInfoAfterFirst.LastWriteTimeUtc;
        var firstLength = fileInfoAfterFirst.Length;

        // Wait long enough that any second write would produce a strictly-later mtime even on
        // filesystems with second-resolution timestamps.
        await Task.Delay(1100);

        await store.UpdateRobloxUserIdAsync(added.Id, 42L); // same value — should be no-op

        var fileInfoAfterSecond = new FileInfo(_filePath);
        Assert.Equal(firstWriteTime, fileInfoAfterSecond.LastWriteTimeUtc);
        Assert.Equal(firstLength, fileInfoAfterSecond.Length);

        var list = await store.ListAsync();
        Assert.Equal(42L, list[0].RobloxUserId);
    }

    [Fact]
    public async Task UpdateRobloxUserIdAsync_ThrowsKeyNotFound_WhenAccountIdMissing()
    {
        using var store = new AccountStore(_filePath);
        await store.AddAsync("OtherUser", "https://avatar", "fake-cookie");
        var ghostId = Guid.NewGuid(); // never added

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => store.UpdateRobloxUserIdAsync(ghostId, 12345L));
    }
}
