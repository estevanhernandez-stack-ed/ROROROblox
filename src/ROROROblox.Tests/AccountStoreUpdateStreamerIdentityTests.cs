using System.IO;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Contract tests for <see cref="IAccountStore.UpdateStreamerIdentityAsync"/> (streamer mode,
/// 2026-07-10). Mirrors <see cref="AccountStoreUpdateRobloxUserIdTests"/>. Verifies:
/// (1) both fields get set on a previously-null account and survive a round-trip through ListAsync,
/// (2) no-op-write avoidance — calling twice with the same values doesn't re-write the DPAPI blob,
/// (3) KeyNotFoundException on a missing accountId.
/// </summary>
public class AccountStoreUpdateStreamerIdentityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public AccountStoreUpdateStreamerIdentityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-test-update-streamer-{Guid.NewGuid():N}");
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
    public async Task UpdateStreamerIdentityAsync_SetsFields_OnPreviouslyNullAccount()
    {
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");
        Assert.Null(added.StreamerName);
        Assert.Null(added.StreamerAvatarId);

        await store.UpdateStreamerIdentityAsync(added.Id, "CaptainNoodle", "noodle");

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Equal("CaptainNoodle", list[0].StreamerName);
        Assert.Equal("noodle", list[0].StreamerAvatarId);
    }

    [Fact]
    public async Task UpdateStreamerIdentityAsync_IsNoOp_WhenSameValuesAlreadySet()
    {
        // No-op-write avoidance is load-bearing on chatty identity-refresh UIs — an unchanged
        // value must NOT trigger a redundant DPAPI round-trip + disk write.
        using var store = new AccountStore(_filePath);
        var added = await store.AddAsync("TestUser", "https://avatar", "fake-cookie");
        await store.UpdateStreamerIdentityAsync(added.Id, "CaptainNoodle", "noodle");

        // Capture the file's mtime + length after the first write.
        var fileInfoAfterFirst = new FileInfo(_filePath);
        var firstWriteTime = fileInfoAfterFirst.LastWriteTimeUtc;
        var firstLength = fileInfoAfterFirst.Length;

        // Wait long enough that any second write would produce a strictly-later mtime even on
        // filesystems with second-resolution timestamps.
        await Task.Delay(1100);

        await store.UpdateStreamerIdentityAsync(added.Id, "CaptainNoodle", "noodle"); // same — no-op

        var fileInfoAfterSecond = new FileInfo(_filePath);
        Assert.Equal(firstWriteTime, fileInfoAfterSecond.LastWriteTimeUtc);
        Assert.Equal(firstLength, fileInfoAfterSecond.Length);

        var list = await store.ListAsync();
        Assert.Equal("CaptainNoodle", list[0].StreamerName);
        Assert.Equal("noodle", list[0].StreamerAvatarId);
    }

    [Fact]
    public async Task UpdateStreamerIdentityAsync_ThrowsKeyNotFound_WhenAccountIdMissing()
    {
        using var store = new AccountStore(_filePath);
        await store.AddAsync("OtherUser", "https://avatar", "fake-cookie");
        var ghostId = Guid.NewGuid(); // never added

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => store.UpdateStreamerIdentityAsync(ghostId, "Ghost", "phantom"));
    }
}
