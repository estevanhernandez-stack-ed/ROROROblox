using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// A saved vault survives a torn write: <c>accounts.dat.bak</c> holds the previous good file, and
/// <see cref="AccountStore"/> reads it when the main file is missing, empty or unreadable. Real DPAPI,
/// one temp folder per test. Cookies here are obviously fake.
/// </summary>
public class AccountStoreDurabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _mainPath;
    private readonly string _backupPath;

    public AccountStoreDurabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-durability-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _mainPath = Path.Combine(_tempDir, "accounts.dat");
        _backupPath = _mainPath + ".bak";
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // Decrypts a vault file the way the store does. Throws if the file is not a good vault.
    private static string Plaintext(string path)
    {
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private static HashSet<Guid> Ids(string path)
    {
        using var doc = JsonDocument.Parse(Plaintext(path));
        return doc.RootElement.GetProperty("accounts").EnumerateArray()
            .Select(a => a.GetProperty("id").GetGuid())
            .ToHashSet();
    }

    private static byte[] RandomGarbage()
    {
        var bytes = new byte[256];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    private static async Task<HashSet<Guid>> ListedIds(AccountStore store) =>
        (await store.ListAsync()).Select(a => a.Id).ToHashSet();

    [Fact]
    public async Task TwoSaves_LeaveABackupThatDecryptsToTheEarlierState()
    {
        using var store = new AccountStore(_mainPath);
        var a = await store.AddAsync("UserA", "https://x", "fake-cookie-a");
        var b = await store.AddAsync("UserB", "https://x", "fake-cookie-b");

        Assert.Equal(new HashSet<Guid> { a.Id }, Ids(_backupPath));
        Assert.Equal(new HashSet<Guid> { a.Id, b.Id }, Ids(_mainPath));
    }

    [Fact]
    public async Task ZeroLengthMain_LoadsTheBackupsAccounts_AndLogsOneWarning()
    {
        var log = new CapturingLogger<AccountStore>();
        using var store = new AccountStore(_mainPath, log);
        var a = await store.AddAsync("UserA", "https://x", "fake-cookie-a");
        await store.AddAsync("UserB", "https://x", "fake-cookie-b");
        File.WriteAllBytes(_mainPath, []);

        var ids = await ListedIds(store);

        Assert.Equal(new HashSet<Guid> { a.Id }, ids);
        var warning = Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("1", warning);
        Assert.DoesNotContain("fake-cookie", string.Join('\n', log.Snapshot()));
    }

    [Fact]
    public async Task RandomBytesMain_WithAGoodBackup_LoadsTheBackupsAccounts()
    {
        using var store = new AccountStore(_mainPath);
        var a = await store.AddAsync("UserA", "https://x", "fake-cookie-a");
        await store.AddAsync("UserB", "https://x", "fake-cookie-b");
        File.WriteAllBytes(_mainPath, RandomGarbage());

        var ids = await ListedIds(store);

        Assert.Equal(new HashSet<Guid> { a.Id }, ids);
    }

    [Fact]
    public async Task RecoveryFromAReadOnlyCall_HealsTheMainFile_AndWarnsOnlyOnce()
    {
        var log = new CapturingLogger<AccountStore>();
        using var store = new AccountStore(_mainPath, log);
        var a = await store.AddAsync("UserA", "https://x", "fake-cookie-a");
        await store.AddAsync("UserB", "https://x", "fake-cookie-b");
        File.WriteAllBytes(_mainPath, []);

        await store.ListAsync();

        Assert.Equal(new HashSet<Guid> { a.Id }, Ids(_mainPath));   // healed from the backup
        Assert.Equal(new HashSet<Guid> { a.Id }, Ids(_backupPath)); // backup untouched
        await store.ListAsync();
        Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZeroLengthMain_WithNoBackup_WarnsOnceAcrossLoads()
    {
        var log = new CapturingLogger<AccountStore>();
        using var store = new AccountStore(_mainPath, log);
        File.WriteAllBytes(_mainPath, []);

        await store.ListAsync();
        await store.ListAsync();

        Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStaleTempFile_DoesNotBreakTheNextSave_AndIsNotLeftBehind()
    {
        var tempPath = _mainPath + ".tmp";
        File.WriteAllBytes(tempPath, RandomGarbage());
        using var store = new AccountStore(_mainPath);

        var a = await store.AddAsync("UserA", "https://x", "fake-cookie-a");
        var b = await store.AddAsync("UserB", "https://x", "fake-cookie-b"); // second save takes the Replace path

        Assert.Equal(new HashSet<Guid> { a.Id, b.Id }, Ids(_mainPath));
        Assert.Equal(new HashSet<Guid> { a.Id }, Ids(_backupPath));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task RandomBytesMain_WithNoBackup_StillThrowsCorrupt()
    {
        using var store = new AccountStore(_mainPath);
        await store.AddAsync("UserA", "https://x", "fake-cookie-a");
        Assert.False(File.Exists(_backupPath)); // first write has nothing to back up
        File.WriteAllBytes(_mainPath, RandomGarbage());

        await Assert.ThrowsAsync<AccountStoreCorruptException>(() => store.ListAsync());
    }

    [Fact]
    public async Task ZeroLengthMain_WithNoBackup_StillReadsAsEmpty_AndLogsAWarning()
    {
        var log = new CapturingLogger<AccountStore>();
        using var store = new AccountStore(_mainPath, log);
        File.WriteAllBytes(_mainPath, []);

        var list = await store.ListAsync();

        Assert.Empty(list);
        Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingMain_WithABackup_LoadsTheBackupsAccounts()
    {
        using var store = new AccountStore(_mainPath);
        var a = await store.AddAsync("UserA", "https://x", "fake-cookie-a");
        await store.AddAsync("UserB", "https://x", "fake-cookie-b");
        File.Delete(_mainPath);

        var ids = await ListedIds(store);

        Assert.Equal(new HashSet<Guid> { a.Id }, ids);
    }

    [Theory]
    [InlineData(false)] // zero-length main
    [InlineData(true)]  // random-bytes main
    public async Task AfterARecoveryAndOneMoreWrite_BothFilesDecrypt_AndNeitherIsTheDamagedFile(bool randomBytes)
    {
        using var store = new AccountStore(_mainPath);
        var a = await store.AddAsync("UserA", "https://x", "fake-cookie-a");
        await store.AddAsync("UserB", "https://x", "fake-cookie-b");
        var damaged = randomBytes ? RandomGarbage() : [];
        File.WriteAllBytes(_mainPath, damaged);

        var c = await store.AddAsync("UserC", "https://x", "fake-cookie-c"); // loads the backup, then saves

        Assert.Equal(new HashSet<Guid> { a.Id, c.Id }, Ids(_mainPath));
        Assert.Equal(new HashSet<Guid> { a.Id }, Ids(_backupPath)); // the good backup, not promoted over
        Assert.NotEqual(damaged, File.ReadAllBytes(_backupPath));
        Assert.NotEqual(damaged, File.ReadAllBytes(_mainPath));
    }

    [Fact]
    public async Task RemoveAsync_LeavesTheRemovedAccountInNeitherFile()
    {
        using var store = new AccountStore(_mainPath);
        var a = await store.AddAsync("UserA", "https://x", "fake-cookie-a");
        var b = await store.AddAsync("UserB", "https://x", "fake-cookie-b");
        var c = await store.AddAsync("UserC", "https://x", "fake-cookie-c");

        await store.RemoveAsync(b.Id);

        Assert.Equal(new HashSet<Guid> { a.Id, c.Id }, Ids(_mainPath));
        Assert.Equal(new HashSet<Guid> { a.Id, c.Id }, Ids(_backupPath));
        Assert.DoesNotContain("fake-cookie-b", Plaintext(_mainPath));
        Assert.DoesNotContain("fake-cookie-b", Plaintext(_backupPath));
    }

    [Fact]
    public void SaveAsync_FlushesTheTempFileToDisk_BeforeTheSwap()
    {
        var root = XamlStyleScanner.FindRepoRoot();
        Assert.False(root is null, "Could not locate ROROROblox.slnx.");
        var source = File.ReadAllText(Path.Combine(root!, "src", "ROROROblox.Core", "AccountStore.cs"));

        var start = source.IndexOf("private async Task SaveAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "SaveAsync is gone from AccountStore.cs.");
        var end = source.IndexOf("\n    private ", start + 1, StringComparison.Ordinal); // the next member after SaveAsync
        Assert.True(end > start, "Could not bound SaveAsync's body.");
        var body = source[start..end];

        var flush = Math.Min(
            IndexOrMax(body, "Flush(true)"),
            IndexOrMax(body, "flushToDisk: true"));
        var swap = Math.Min(
            IndexOrMax(body, "File.Replace("),
            IndexOrMax(body, "File.Move("));

        Assert.True(flush != int.MaxValue,
            "SaveAsync no longer flushes the temp file to disk (Flush(true) or flushToDisk: true). "
            + "Without it the swap can land a main file whose bytes never reached the platter, the torn write this fence exists for.");
        Assert.True(swap != int.MaxValue, "SaveAsync no longer swaps the temp file in.");
        Assert.True(flush < swap, "SaveAsync must flush the temp file to disk before File.Replace/File.Move swaps it in.");
    }

    private static int IndexOrMax(string text, string needle)
    {
        var i = text.IndexOf(needle, StringComparison.Ordinal);
        return i < 0 ? int.MaxValue : i;
    }
}
