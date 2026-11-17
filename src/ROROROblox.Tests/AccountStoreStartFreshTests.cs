using System.IO;
using System.Security.Cryptography;
using System.Text;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Start fresh always starts fresh, or says it could not. Since v1.39 every vault write reads first, so a
/// damaged <c>accounts.dat</c> that the dialog failed to rename aside made the next add throw the corrupt
/// exception again and brought the dialog back on every start. <see cref="AccountStore.StartFreshAsync"/>
/// sets the vault and its backup aside under timestamped names, never deletes, and reports the file it could
/// not move. Each case ends with an empty list, a successful add, the original bytes still on disk under a
/// set-aside name, and nothing coming back from a backup the app had refused. Real DPAPI, one temp folder
/// per test, obviously fake cookies.
/// </summary>
public sealed class AccountStoreStartFreshTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rororo-startfresh-{Guid.NewGuid():N}");
    private readonly string _main;
    private readonly string _backup;

    public AccountStoreStartFreshTests()
    {
        Directory.CreateDirectory(_dir);
        _main = Path.Combine(_dir, "accounts.dat");
        _backup = _main + ".bak";
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static readonly byte[] Junk = [1, 2, 3, 4, 5, 6, 7, 8];
    private static readonly byte[] OtherJunk = [9, 9, 9, 9, 9, 9, 9, 9];

    private static byte[] GoodVault(string name, string cookie) => ProtectedData.Protect(Encoding.UTF8.GetBytes(
        "{\"version\":1,\"accounts\":[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"displayName\":\"" + name
        + "\",\"avatarUrl\":\"\",\"cookie\":\"" + cookie + "\",\"createdAt\":\"2026-01-01T00:00:00+00:00\"}]}"),
        optionalEntropy: null, DataProtectionScope.CurrentUser);

    private string[] Files() => Directory.GetFiles(_dir).Select(f => Path.GetFileName(f)!).OrderBy(f => f, StringComparer.Ordinal).ToArray();

    /// <summary>The set-aside copy of <paramref name="original"/>, wherever the timestamp landed.</summary>
    private string Aside(string original) =>
        Assert.Single(Directory.GetFiles(_dir, Path.GetFileName(original) + ".corrupt-*"));

    private async Task AssertStartsEmptyAndAcceptsAnAdd(AccountStore store)
    {
        Assert.Empty(await store.ListAsync());
        var added = await store.AddAsync("New", "", "fake-cookie-new");
        Assert.Equal(added.Id, Assert.Single(await store.ListAsync()).Id);
        Assert.Equal("fake-cookie-new", await store.RetrieveCookieAsync(added.Id));
    }

    [Fact]
    public async Task DamagedMain_NoBackup_SetsMainAside_StartsEmpty_AndAcceptsAnAdd()
    {
        File.WriteAllBytes(_main, Junk);
        using var store = new AccountStore(_main);
        await Assert.ThrowsAsync<AccountStoreCorruptException>(() => store.AddAsync("A", "", "fake-cookie-a"));

        var result = await store.StartFreshAsync();

        Assert.True(result.Succeeded);
        Assert.Empty(result.Failed);
        Assert.Equal(Junk, File.ReadAllBytes(Aside(_main)));
        Assert.False(File.Exists(_main));
        await AssertStartsEmptyAndAcceptsAnAdd(store);
        Assert.Equal(Junk, File.ReadAllBytes(Aside(_main))); // the add did not touch it
    }

    [Fact]
    public async Task DamagedMain_DamagedBackup_SetsBothAside_StartsEmpty_AndAcceptsAnAdd()
    {
        File.WriteAllBytes(_main, Junk);
        File.WriteAllBytes(_backup, OtherJunk);
        using var store = new AccountStore(_main);

        var result = await store.StartFreshAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.SetAside.Count);
        Assert.Equal(Junk, File.ReadAllBytes(Aside(_main)));
        Assert.Equal(OtherJunk, File.ReadAllBytes(Aside(_backup)));
        Assert.False(File.Exists(_backup));
        await AssertStartsEmptyAndAcceptsAnAdd(store);
    }

    [Fact]
    public async Task DamagedMain_ThatCannotBeSetAside_ReportsTheFile_ChangesNothing_AndASecondTryWorksOnceItIsFree()
    {
        File.WriteAllBytes(_main, Junk);
        File.WriteAllBytes(_backup, OtherJunk);
        using var store = new AccountStore(_main);

        StartFreshResult blocked;
        using (new FileStream(_main, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            blocked = await store.StartFreshAsync();
        }

        Assert.False(blocked.Succeeded);
        Assert.Equal(["accounts.dat"], blocked.Failed);
        Assert.Empty(blocked.SetAside);
        // Nothing it could not undo: the backup it had already moved is back under its own name.
        Assert.Equal(["accounts.dat", "accounts.dat.bak"], Files());
        Assert.Equal(Junk, File.ReadAllBytes(_main));
        Assert.Equal(OtherJunk, File.ReadAllBytes(_backup));
        // And the store is exactly as stuck as before: the dialog has to stay open and say so.
        await Assert.ThrowsAsync<AccountStoreCorruptException>(() => store.AddAsync("A", "", "fake-cookie-a"));

        var retry = await store.StartFreshAsync();

        Assert.True(retry.Succeeded);
        await AssertStartsEmptyAndAcceptsAnAdd(store);
        Assert.Equal(Junk, File.ReadAllBytes(Aside(_main)));
    }

    [Fact]
    public async Task ABackupThatCannotBeSetAside_ChangesNothing()
    {
        File.WriteAllBytes(_main, Junk);
        File.WriteAllBytes(_backup, OtherJunk);
        using var store = new AccountStore(_main);

        StartFreshResult blocked;
        using (new FileStream(_backup, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            blocked = await store.StartFreshAsync();
        }

        Assert.Equal(["accounts.dat.bak"], blocked.Failed);
        Assert.Equal(["accounts.dat", "accounts.dat.bak"], Files());
        Assert.Equal(Junk, File.ReadAllBytes(_main));
    }

    [Fact]
    public async Task MissingMain_BesideADamagedBackup_ReadsAsEmpty_AndAcceptsAnAdd()
    {
        // What the store does by itself, before any Start fresh: a damaged backup is not a source of accounts.
        File.WriteAllBytes(_backup, Junk);
        using var store = new AccountStore(_main);

        await AssertStartsEmptyAndAcceptsAnAdd(store);

        Assert.Equal(Junk, File.ReadAllBytes(_backup)); // an add with no good main read leaves the backup alone
    }

    [Fact]
    public async Task MissingMain_BesideAGoodStaleBackup_BringsTheStaleAccountsBack()
    {
        // The store's own rule, and the reason Start fresh sets the backup aside too: with main gone and the
        // backup left in place, the next read restores accounts the user had just refused to keep.
        File.WriteAllBytes(_backup, GoodVault("Stale", "fake-cookie-stale"));
        using var store = new AccountStore(_main);

        Assert.Equal("Stale", Assert.Single(await store.ListAsync()).DisplayName);
    }

    [Fact]
    public async Task AStoreThatReadsCorrectly_IsNotStartedFresh_NothingIsSetAside()
    {
        // Start fresh looks again before it sets anything aside. A store that reads by its ordinary rules
        // (here: main gone, the backup good) has nothing to start fresh from, and hiding its accounts
        // behind a rename would be the loss the dialog is supposed to prevent.
        var backupBytes = GoodVault("Kept", "fake-cookie-kept");
        File.WriteAllBytes(_backup, backupBytes);
        using var store = new AccountStore(_main);

        var result = await store.StartFreshAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.StoreWasReadable);
        Assert.Empty(result.SetAside);
        Assert.Empty(Directory.GetFiles(_dir, "*.corrupt-*"));
        Assert.Equal("Kept", Assert.Single(await store.ListAsync()).DisplayName);
    }

    [Fact]
    public async Task AStoreThatStillCannotBeRead_IsStartedFresh_AndSaysItWasNotReadable()
    {
        File.WriteAllBytes(_main, OtherJunk);
        using var store = new AccountStore(_main);

        var result = await store.StartFreshAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.StoreWasReadable);
        Assert.Single(result.SetAside);
    }

    [Fact]
    public async Task ALockedGoodMainBesideAGoodBackup_IsTheCaseWhereTheDialogShowsWithAGoodBackup()
    {
        // A read that fails on I/O throws the corrupt exception without trying the backup, so the dialog can
        // appear over a vault that is perfectly good. While the lock holds, Start fresh reports the file
        // it could not move and changes nothing; once the lock is gone it finds the vault readable.
        File.WriteAllBytes(_main, GoodVault("Main", "fake-cookie-main"));
        File.WriteAllBytes(_backup, GoodVault("Stale", "fake-cookie-stale"));
        using var store = new AccountStore(_main);

        using (var held = new FileStream(_main, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAsync<AccountStoreCorruptException>(() => store.ListAsync());
            var blocked = await store.StartFreshAsync();
            Assert.False(blocked.Succeeded);
            Assert.Equal(["accounts.dat"], blocked.Failed);
        }

        // The lock is gone, and the vault it was guarding is fine. This is the case that matters most:
        // the dialog is still on screen, the user clicks Start fresh, and every saved account must stay.
        var result = await store.StartFreshAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.StoreWasReadable);
        Assert.Empty(result.SetAside);
        Assert.Empty(Directory.GetFiles(_dir, "*.corrupt-*"));
        Assert.Equal("Main", Assert.Single(await store.ListAsync()).DisplayName);
        Assert.Equal("fake-cookie-main", await store.RetrieveCookieAsync((await store.ListAsync())[0].Id));
    }

    [Fact]
    public async Task NothingToSetAside_Succeeds_AndChangesNothing()
    {
        using var store = new AccountStore(_main);

        var result = await store.StartFreshAsync();

        Assert.True(result.Succeeded);
        Assert.Empty(result.SetAside);
        Assert.Empty(Files());
    }

    [Fact]
    public async Task SetAsideFiles_StayBesideTheVault_AndNeverReplaceEachOther()
    {
        using var store = new AccountStore(_main);
        File.WriteAllBytes(_main, Junk);
        await store.StartFreshAsync();
        File.WriteAllBytes(_main, OtherJunk);

        var second = await store.StartFreshAsync(); // same second, same stamp

        Assert.True(second.Succeeded);
        var asides = Directory.GetFiles(_dir, "accounts.dat.corrupt-*");
        Assert.Equal(2, asides.Length);
        Assert.Equal(new[] { Junk, OtherJunk }.Select(b => Convert.ToHexString(b)).OrderBy(h => h), asides.Select(a => Convert.ToHexString(File.ReadAllBytes(a))).OrderBy(h => h));
        Assert.All(Directory.GetFiles(_dir), f => Assert.Equal(_dir, Path.GetDirectoryName(f)));
    }

    [Fact]
    public async Task StartFresh_IsNotASilentFailure_TheResultNamesWhatFailed()
    {
        // The old dialog swallowed a failed rename. The method that replaced it must not: this is the row the
        // break-it check turns red (make TryMove report success and the Failed list goes empty).
        File.WriteAllBytes(_main, Junk);
        using var store = new AccountStore(_main);
        using var held = new FileStream(_main, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = await store.StartFreshAsync();

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Failed);
    }
}
