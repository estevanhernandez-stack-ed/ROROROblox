using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// A vault that decrypts and parses but has the wrong shape must never read as "no accounts". An empty
/// list read as good lets the next save overwrite every saved account, and lets that empty file replace
/// <c>accounts.dat.bak</c>. So a wrong-shape main goes where an undecryptable one goes: to the backup
/// when there is one (and heals main from it), otherwise to <see cref="AccountStoreCorruptException"/>.
/// Real DPAPI, one temp folder per test, obviously fake cookies.
/// </summary>
public sealed class AccountStoreWrongShapeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rororo-vault-shape-{Guid.NewGuid():N}");
    private readonly string _main;
    private readonly string _backup;

    public AccountStoreWrongShapeTests()
    {
        Directory.CreateDirectory(_dir);
        _main = Path.Combine(_dir, "accounts.dat");
        _backup = _main + ".bak";
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Row = """{"id":"00000000-0000-0000-0000-000000000001","displayName":"A","avatarUrl":"","cookie":"fake-cookie-a","createdAt":"2026-01-01T00:00:00+00:00"}""";

    // Every wrong shape the vault can decrypt to. "{}" and the version-only blob leave a null list, the
    // literal null used to come back as a GOOD empty vault, "[]" and the null-row shapes are the rest.
    public static IEnumerable<object[]> WrongShapes() =>
    [
        ["{}"],
        ["""{"version":1}"""],
        ["null"],
        ["[]"],
        ["""{"version":1,"accounts":null}"""],
        ["""{"version":1,"accounts":[null]}"""],
        ["""{"version":1,"accounts":[""" + Row + ",null]}"],
    ];

    private static byte[] Protect(string json) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(json), optionalEntropy: null, DataProtectionScope.CurrentUser);

    private static string Plaintext(string path) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), optionalEntropy: null, DataProtectionScope.CurrentUser));

    [Theory]
    [MemberData(nameof(WrongShapes))]
    public async Task WrongShapeMain_WithNoBackup_IsTheCorruptPath_AndNothingIsWritten(string shape)
    {
        var bytes = Protect(shape);
        File.WriteAllBytes(_main, bytes);
        using var store = new AccountStore(_main);

        await Assert.ThrowsAsync<AccountStoreCorruptException>(() => store.ListAsync());
        // A write starts with a read, so it is refused the same way and cannot reach the save.
        await Assert.ThrowsAsync<AccountStoreCorruptException>(() => store.AddAsync("B", "", "fake-cookie-b"));
        await Assert.ThrowsAsync<AccountStoreCorruptException>(() => store.SetSelectedAsync(Guid.NewGuid(), false));
        await Assert.ThrowsAsync<AccountStoreCorruptException>(() => store.RemoveAsync(Guid.NewGuid()));

        Assert.Equal(bytes, File.ReadAllBytes(_main));
        Assert.False(File.Exists(_backup));
        Assert.False(File.Exists(_main + ".tmp"));
    }

    [Theory]
    [MemberData(nameof(WrongShapes))]
    public async Task WrongShapeMain_WithAGoodBackup_LoadsTheBackup_HealsMain_AndLeavesTheBackup(string shape)
    {
        using var store = new AccountStore(_main);
        var a = await store.AddAsync("UserA", "", "fake-cookie-a");
        await store.AddAsync("UserB", "", "fake-cookie-b"); // second save leaves the first state as the backup
        var backupBytes = File.ReadAllBytes(_backup);
        File.WriteAllBytes(_main, Protect(shape));

        var listed = await store.ListAsync();

        Assert.Equal(a.Id, Assert.Single(listed).Id); // the backup's accounts, never an empty list
        using (var doc = JsonDocument.Parse(Plaintext(_main)))
        {
            Assert.Single(doc.RootElement.GetProperty("accounts").EnumerateArray()); // main healed from the backup
        }
        Assert.Equal(backupBytes, File.ReadAllBytes(_backup));

        // And a write in the same state keeps the account and never promotes the damaged main over the backup.
        var c = await store.AddAsync("UserC", "", "fake-cookie-c");
        Assert.Equal(new HashSet<Guid> { a.Id, c.Id }, (await store.ListAsync()).Select(x => x.Id).ToHashSet());
        Assert.Equal(backupBytes, File.ReadAllBytes(_backup));
    }

    [Fact]
    public async Task ANullVault_IsNoLongerAGoodEmptyOne_ItDoesNotReplaceTheBackup()
    {
        // The case that lost accounts: "null" parsed to a null blob, was returned as a good empty store, and
        // the next save moved that empty main over the backup.
        using var store = new AccountStore(_main);
        var a = await store.AddAsync("UserA", "", "fake-cookie-a");
        await store.AddAsync("UserB", "", "fake-cookie-b");
        File.WriteAllBytes(_main, Protect("null"));

        await store.SetSelectedAsync(a.Id, false); // a write from the damaged state

        var ids = (await store.ListAsync()).Select(x => x.Id).ToHashSet();
        Assert.Contains(a.Id, ids);
        Assert.Contains("fake-cookie-a", Plaintext(_backup));
    }

    [Fact]
    public async Task AHealthyEmptyVault_StillReadsAsEmpty_WithNoException()
    {
        File.WriteAllBytes(_main, Protect("""{"version":1,"accounts":[]}"""));
        using var store = new AccountStore(_main);

        Assert.Empty(await store.ListAsync());
    }

    // A row with a field missing is not a damaged vault. Rejecting it would put every account in the
    // file behind the corrupt dialog to guard against one bad row, so it is repaired to empty strings:
    // the row loads, reads as signed out, and the accounts beside it are untouched.
    [Theory]
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000002","displayName":"B","createdAt":"2026-01-01T00:00:00+00:00"}""", "B", "")] // no cookie, no avatar
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000002","cookie":"fake-cookie-b","createdAt":"2026-01-01T00:00:00+00:00"}""", "", "fake-cookie-b")] // no name
    [InlineData("""{"id":"00000000-0000-0000-0000-000000000002","displayName":null,"avatarUrl":null,"cookie":null,"createdAt":"2026-01-01T00:00:00+00:00"}""", "", "")] // explicit nulls
    public async Task ARowWithAMissingField_IsRepairedToEmpty_AndTheVaultStillOpens(string badRow, string expectedName, string expectedCookie)
    {
        File.WriteAllBytes(_main, Protect("""{"version":1,"accounts":[""" + Row + "," + badRow + "]}"));
        var mainBefore = File.ReadAllBytes(_main);
        using var store = new AccountStore(_main);

        var listed = await store.ListAsync();

        Assert.Equal(2, listed.Count);
        Assert.Equal("A", listed[0].DisplayName);
        Assert.Equal("fake-cookie-a", await store.RetrieveCookieAsync(listed[0].Id));
        Assert.Equal(expectedName, listed[1].DisplayName);
        Assert.NotNull(listed[1].AvatarUrl);
        Assert.Equal(expectedCookie, await store.RetrieveCookieAsync(listed[1].Id));
        // A read repairs in memory only: the file is not rewritten and no backup appears.
        Assert.Equal(mainBefore, File.ReadAllBytes(_main));
        Assert.False(File.Exists(_backup));
    }

    [Fact]
    public async Task ARowWithAMissingField_DoesNotSendAGoodMainToTheBackup()
    {
        // The backup is older and holds only A. Main holds A and a row with no cookie. Main must win:
        // falling back would silently drop the newer row.
        File.WriteAllBytes(_backup, Protect("""{"version":1,"accounts":[""" + Row + "]}"));
        File.WriteAllBytes(_main, Protect("""{"version":1,"accounts":[""" + Row + """,{"id":"00000000-0000-0000-0000-000000000002","displayName":"B","createdAt":"2026-01-01T00:00:00+00:00"}]}"""));
        using var store = new AccountStore(_main);

        var listed = await store.ListAsync();

        Assert.Equal(["A", "B"], listed.Select(a => a.DisplayName));
    }

    [Fact]
    public async Task AHealthyVault_StillRoundTrips()
    {
        File.WriteAllBytes(_main, Protect("""{"version":1,"accounts":[""" + Row + "]}"));
        using var store = new AccountStore(_main);

        var listed = await store.ListAsync();

        Assert.Equal("A", Assert.Single(listed).DisplayName);
        Assert.Equal("fake-cookie-a", await store.RetrieveCookieAsync(listed[0].Id));
    }

    [Fact]
    public async Task WrongShapeWithABackup_LogsOneWarning_WithTheFileNameAndNoCookie()
    {
        var log = new CapturingLogger<AccountStore>();
        using var store = new AccountStore(_main, log);
        await store.AddAsync("UserA", "", "fake-cookie-a");
        await store.AddAsync("UserB", "", "fake-cookie-b");
        File.WriteAllBytes(_main, Protect("""{"version":1}"""));

        await store.ListAsync();
        await store.ListAsync();

        var warning = Assert.Single(log.Snapshot(), l => l.StartsWith("[Warning]", StringComparison.Ordinal));
        Assert.Contains("accounts.dat", warning);
        Assert.DoesNotContain("fake-cookie", string.Join('\n', log.Snapshot()));
        Assert.DoesNotContain(_dir, string.Join('\n', log.Snapshot()));
    }
}
