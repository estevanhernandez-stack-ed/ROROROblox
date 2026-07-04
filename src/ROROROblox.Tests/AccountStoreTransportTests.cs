using System.IO;
using ROROROblox.Core;
using ROROROblox.Core.Transport;

namespace ROROROblox.Tests;

/// <summary>
/// v1.6.0 account-transport store integration (spec §1). Covers the bulk export read
/// (<see cref="AccountStore.ExportAccountsAsync"/>) + merge-by-userId import
/// (<see cref="AccountStore.ImportMergeAsync"/>), including a full round-trip through the real
/// <see cref="AccountTransportService"/> crypto (store A → Export bytes → Import → merge into
/// store B). SECURITY-SENSITIVE: cookies appear in plaintext only transiently; test fixtures use
/// obvious FAKE-COOKIE-* placeholders so the pre-commit secret scan stays green.
///
/// Each test owns a unique temp directory so concurrent runs don't collide.
/// </summary>
public class AccountStoreTransportTests : IDisposable
{
    private readonly string _tempDir;

    public AccountStoreTransportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-transport-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private string PathFor(string name) => Path.Combine(_tempDir, name);

    // ---------- ExportAccountsAsync ----------

    [Fact]
    public async Task ExportAccountsAsync_BuildsRecords_ForIdsWithUserId()
    {
        using var store = new AccountStore(PathFor("a.dat"));
        var a = await store.AddAsync("Alpha", "https://avatar/a", "FAKE-COOKIE-alpha-AAAA");
        var b = await store.AddAsync("Beta", "https://avatar/b", "FAKE-COOKIE-beta-BBBB");
        await store.UpdateRobloxUserIdAsync(a.Id, 111L);
        await store.UpdateRobloxUserIdAsync(b.Id, 222L);

        var result = await store.ExportAccountsAsync(new[] { a.Id, b.Id });

        Assert.Equal(2, result.Records.Count);
        Assert.Empty(result.SkippedNoUserId);
        Assert.Contains(result.Records, r => r.RobloxUserId == 111L);
        Assert.Contains(result.Records, r => r.RobloxUserId == 222L);
    }

    [Fact]
    public void AccountExportRecord_DoesNotCarryBrowserTrackerId()
    {
        // Canary for the v1.8.1 "btid is client-instance identity, not transported" invariant
        // (followups 2026-06-30 §6). The btid deliberately stays OUT of the export record so a
        // destination PC generates its own — two machines must never launch the same account
        // with the same tracker id simultaneously. This is enforced only by omission (the
        // record simply lacks the field), so the natural instinct when extending transport is
        // to add it alongside the other per-account fields. This test goes red if someone does.
        var props = typeof(AccountExportRecord).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain("BrowserTrackerId", props);
    }

    // ---------- At-rest encryption (item-4 security gate) ----------

    [Fact]
    public async Task ImportedAccounts_AreDpapiEncryptedAtRest_NotPlaintextOnDisk()
    {
        // Export from store A, import into a fresh store B, then read B's raw .dat bytes and confirm
        // the cookie never appears in plaintext on disk — i.e. ImportMergeAsync routed through the
        // DPAPI SaveAsync path, not an accidental File.WriteAllText of the records.
        const string secretCookie = "FAKE-COOKIE-secret-ZZZZ";
        using (var a = new AccountStore(PathFor("a.dat")))
        {
            var acc = await a.AddAsync("Alpha", "https://avatar/a", secretCookie);
            await a.UpdateRobloxUserIdAsync(acc.Id, 111L);
            var export = await a.ExportAccountsAsync(new[] { acc.Id });

            var bundle = new AccountTransportService().Export(export.Records, "a-strong-passphrase-123");
            var records = new AccountTransportService().Import(bundle, "a-strong-passphrase-123");

            var bPath = PathFor("b.dat");
            using var b = new AccountStore(bPath);
            await b.ImportMergeAsync(records);

            var raw = await File.ReadAllBytesAsync(bPath);
            var asText = System.Text.Encoding.UTF8.GetString(raw);
            Assert.DoesNotContain(secretCookie, asText); // DPAPI ciphertext, not plaintext JSON
            // ...but the cookie still round-trips back out via the decrypt path.
            var imported = (await b.ListAsync()).Single(x => x.RobloxUserId == 111L);
            Assert.Equal(secretCookie, await b.RetrieveCookieAsync(imported.Id));
        }
    }

    [Fact]
    public async Task ExportAccountsAsync_SkipsAccountWithNullUserId()
    {
        using var store = new AccountStore(PathFor("a.dat"));
        var withId = await store.AddAsync("HasId", "https://avatar/h", "FAKE-COOKIE-hasid-AAAA");
        var noId = await store.AddAsync("NoId", "https://avatar/n", "FAKE-COOKIE-noid-BBBB");
        await store.UpdateRobloxUserIdAsync(withId.Id, 999L);
        // noId deliberately never gets a userId.

        var result = await store.ExportAccountsAsync(new[] { withId.Id, noId.Id });

        Assert.Single(result.Records);
        Assert.Equal(999L, result.Records[0].RobloxUserId);
        Assert.Single(result.SkippedNoUserId);
        Assert.Equal(noId.Id, result.SkippedNoUserId[0]);
        // The skipped account is NOT in the records list.
        Assert.DoesNotContain(result.Records, r => r.DisplayName == "NoId");
    }

    [Fact]
    public async Task ExportAccountsAsync_RecordCarriesEveryField_CookieDecrypted()
    {
        using var store = new AccountStore(PathFor("a.dat"));
        var acct = await store.AddAsync("FullGuy", "https://avatar/full.png", "FAKE-COOKIE-full-CCCC");
        await store.UpdateRobloxUserIdAsync(acct.Id, 4242L);
        await store.SetMainAsync(acct.Id);
        await store.SetFpsCapAsync(acct.Id, 144);
        await store.SetCaptionColorAsync(acct.Id, "#f22f89");
        await store.UpdateLocalNameAsync(acct.Id, "Captain");
        await store.SetTagsAsync(acct.Id, new[] { "PS99", "RCU" });
        await store.SetSelectedAsync(acct.Id, false);

        var result = await store.ExportAccountsAsync(new[] { acct.Id });
        var record = Assert.Single(result.Records);

        Assert.Equal("FullGuy", record.DisplayName);
        Assert.Equal("https://avatar/full.png", record.AvatarUrl);
        Assert.Equal(4242L, record.RobloxUserId);
        Assert.Equal("FAKE-COOKIE-full-CCCC", record.Cookie);   // decrypted via DPAPI path
        Assert.Equal(new[] { "PS99", "RCU" }, record.Tags);
        Assert.Equal(144, record.FpsCap);
        Assert.Equal("#f22f89", record.CaptionColorHex);
        Assert.Equal("Captain", record.LocalName);
        Assert.True(record.IsMain);
        Assert.Equal(acct.SortOrder, record.SortOrder);
        Assert.False(record.IsSelected);
    }

    [Fact]
    public async Task ExportAccountsAsync_NullTags_BecomeEmptyList()
    {
        using var store = new AccountStore(PathFor("a.dat"));
        var acct = await store.AddAsync("NoTags", "https://avatar/x", "FAKE-COOKIE-notags-DDDD");
        await store.UpdateRobloxUserIdAsync(acct.Id, 7L);

        var result = await store.ExportAccountsAsync(new[] { acct.Id });
        var record = Assert.Single(result.Records);

        Assert.NotNull(record.Tags);
        Assert.Empty(record.Tags);
    }

    [Fact]
    public async Task ExportAccountsAsync_UnknownId_SilentlyIgnored()
    {
        using var store = new AccountStore(PathFor("a.dat"));
        var acct = await store.AddAsync("Real", "https://avatar/r", "FAKE-COOKIE-real-EEEE");
        await store.UpdateRobloxUserIdAsync(acct.Id, 55L);

        var result = await store.ExportAccountsAsync(new[] { acct.Id, Guid.NewGuid() });

        Assert.Single(result.Records);
        Assert.Empty(result.SkippedNoUserId); // unknown id isn't a "no userId" skip
    }

    // ---------- ImportMergeAsync ----------

    [Fact]
    public async Task ImportMergeAsync_AddsAccounts_WhoseUserIdNotPresent()
    {
        using var store = new AccountStore(PathFor("b.dat"));

        var records = new[]
        {
            Record("New1", 1001L, "FAKE-COOKIE-new1-AAAA"),
            Record("New2", 1002L, "FAKE-COOKIE-new2-BBBB"),
        };

        var result = await store.ImportMergeAsync(records);

        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);
        var list = await store.ListAsync();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, a => a.RobloxUserId == 1001L);
        Assert.Contains(list, a => a.RobloxUserId == 1002L);
    }

    [Fact]
    public async Task ImportMergeAsync_SkipsUserId_AlreadyPresent_KeepsLocalUntouched()
    {
        using var store = new AccountStore(PathFor("b.dat"));
        var existing = await store.AddAsync("LocalDude", "https://avatar/local", "FAKE-COOKIE-local-ORIG");
        await store.UpdateRobloxUserIdAsync(existing.Id, 500L);

        var records = new[]
        {
            // Same userId as the existing local account — must be skipped, local kept.
            Record("ImportedDude", 500L, "FAKE-COOKIE-imported-NEW"),
            // New userId — must be added.
            Record("BrandNew", 600L, "FAKE-COOKIE-brandnew-CCCC"),
        };

        var result = await store.ImportMergeAsync(records);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);

        var list = await store.ListAsync();
        Assert.Equal(2, list.Count);
        // Existing local account is untouched — same id, name, and cookie.
        var local = list.Single(a => a.RobloxUserId == 500L);
        Assert.Equal(existing.Id, local.Id);
        Assert.Equal("LocalDude", local.DisplayName);
        Assert.Equal("FAKE-COOKIE-local-ORIG", await store.RetrieveCookieAsync(local.Id));
    }

    [Fact]
    public async Task ImportMergeAsync_DedupesByUserId_NotDisplayName()
    {
        using var store = new AccountStore(PathFor("b.dat"));
        var existing = await store.AddAsync("SameName", "https://avatar/s", "FAKE-COOKIE-same-AAAA");
        await store.UpdateRobloxUserIdAsync(existing.Id, 700L);

        // Same display name but a DIFFERENT userId — must be added (dedupe is userId-only).
        var records = new[] { Record("SameName", 701L, "FAKE-COOKIE-samename2-BBBB") };

        var result = await store.ImportMergeAsync(records);

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(2, (await store.ListAsync()).Count);
    }

    [Fact]
    public async Task ImportMergeAsync_ImportedAccount_CarriesFullSetup_AndCookie()
    {
        using var store = new AccountStore(PathFor("b.dat"));

        var record = new AccountExportRecord(
            DisplayName: "Imported",
            AvatarUrl: "https://avatar/imp.png",
            RobloxUserId: 8080L,
            Cookie: "FAKE-COOKIE-imp-FULL",
            Tags: new[] { "ALPHA", "BETA" },
            FpsCap: 240,
            CaptionColorHex: "#17d4fa",
            LocalName: "Imp",
            IsMain: true,
            SortOrder: 5,
            IsSelected: false);

        await store.ImportMergeAsync(new[] { record });

        var imported = Assert.Single(await store.ListAsync());
        Assert.NotEqual(Guid.Empty, imported.Id);
        Assert.Equal("Imported", imported.DisplayName);
        Assert.Equal("https://avatar/imp.png", imported.AvatarUrl);
        Assert.Equal(8080L, imported.RobloxUserId);
        Assert.Equal(new[] { "ALPHA", "BETA" }, imported.Tags);
        Assert.Equal(240, imported.FpsCap);
        Assert.Equal("#17d4fa", imported.CaptionColorHex);
        Assert.Equal("Imp", imported.LocalName);
        Assert.True(imported.IsMain);
        Assert.Equal(5, imported.SortOrder);
        Assert.False(imported.IsSelected);
        Assert.True(imported.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal("FAKE-COOKIE-imp-FULL", await store.RetrieveCookieAsync(imported.Id));
    }

    [Fact]
    public async Task ImportMergeAsync_EmptyList_NoOp()
    {
        using var store = new AccountStore(PathFor("b.dat"));
        await store.AddAsync("Existing", "https://avatar/e", "FAKE-COOKIE-existing-AAAA");

        var result = await store.ImportMergeAsync(Array.Empty<AccountExportRecord>());

        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task ImportMergeAsync_TwoRecordsSameUserIdInBatch_OnlyFirstImported()
    {
        using var store = new AccountStore(PathFor("b.dat"));

        var records = new[]
        {
            Record("First", 9000L, "FAKE-COOKIE-first-AAAA"),
            Record("Second", 9000L, "FAKE-COOKIE-second-BBBB"), // dupe userId within the same bundle
        };

        var result = await store.ImportMergeAsync(records);

        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Single(await store.ListAsync());
    }

    // ---------- Full round-trip through the real AccountTransportService ----------

    [Fact]
    public async Task FullRoundTrip_StoreA_Export_Import_MergeIntoStoreB_PreservesEverything()
    {
        const string passphrase = "correct-horse-battery-staple-12";
        var transport = new AccountTransportService();

        // Store A — source machine.
        using var storeA = new AccountStore(PathFor("a.dat"));
        var a1 = await storeA.AddAsync("Main", "https://avatar/main.png", "FAKE-COOKIE-main-ZZZZ");
        var a2 = await storeA.AddAsync("Alt", "https://avatar/alt.png", "FAKE-COOKIE-alt-YYYY");
        await storeA.UpdateRobloxUserIdAsync(a1.Id, 314159L);
        await storeA.UpdateRobloxUserIdAsync(a2.Id, 271828L);
        await storeA.SetMainAsync(a1.Id);
        await storeA.SetFpsCapAsync(a1.Id, 144);
        await storeA.SetCaptionColorAsync(a1.Id, "#17d4fa");
        await storeA.UpdateLocalNameAsync(a1.Id, "TheMain");
        await storeA.SetTagsAsync(a1.Id, new[] { "PS99" });
        await storeA.SetSelectedAsync(a2.Id, false);

        // Export read → crypto → bytes → crypto → records.
        var exportResult = await storeA.ExportAccountsAsync(new[] { a1.Id, a2.Id });
        Assert.Equal(2, exportResult.Records.Count);

        byte[] bundle = transport.Export(exportResult.Records, passphrase);
        var restored = transport.Import(bundle, passphrase);

        // Merge into store B — destination machine (different DPAPI envelope, same user here).
        using var storeB = new AccountStore(PathFor("b.dat"));
        var mergeResult = await storeB.ImportMergeAsync(restored);

        Assert.Equal(2, mergeResult.Imported);
        Assert.Equal(0, mergeResult.Skipped);

        var listB = await storeB.ListAsync();
        Assert.Equal(2, listB.Count);

        var main = listB.Single(a => a.RobloxUserId == 314159L);
        Assert.Equal("Main", main.DisplayName);
        Assert.Equal("https://avatar/main.png", main.AvatarUrl);
        Assert.True(main.IsMain);
        Assert.Equal(144, main.FpsCap);
        Assert.Equal("#17d4fa", main.CaptionColorHex);
        Assert.Equal("TheMain", main.LocalName);
        Assert.Equal(new[] { "PS99" }, main.Tags);
        // Cookies survive the full round-trip and are retrievable from store B's DPAPI.
        Assert.Equal("FAKE-COOKIE-main-ZZZZ", await storeB.RetrieveCookieAsync(main.Id));

        var alt = listB.Single(a => a.RobloxUserId == 271828L);
        Assert.False(alt.IsSelected);
        Assert.Equal("FAKE-COOKIE-alt-YYYY", await storeB.RetrieveCookieAsync(alt.Id));
    }

    [Fact]
    public async Task FullRoundTrip_SecondImport_SkipsAlreadyPresent()
    {
        const string passphrase = "another-strong-passphrase-99";
        var transport = new AccountTransportService();

        using var storeA = new AccountStore(PathFor("a.dat"));
        var a1 = await storeA.AddAsync("Solo", "https://avatar/solo.png", "FAKE-COOKIE-solo-AAAA");
        await storeA.UpdateRobloxUserIdAsync(a1.Id, 42L);

        var exportResult = await storeA.ExportAccountsAsync(new[] { a1.Id });
        var bundle = transport.Export(exportResult.Records, passphrase);

        using var storeB = new AccountStore(PathFor("b.dat"));
        var first = await storeB.ImportMergeAsync(transport.Import(bundle, passphrase));
        Assert.Equal(1, first.Imported);

        // Importing the same bundle again is a no-op merge.
        var second = await storeB.ImportMergeAsync(transport.Import(bundle, passphrase));
        Assert.Equal(0, second.Imported);
        Assert.Equal(1, second.Skipped);
        Assert.Single(await storeB.ListAsync());
    }

    // ---------- Existing accounts.dat unaffected ----------

    [Fact]
    public async Task ImportMergeAsync_LeavesPriorAccountsAndCookiesIntact()
    {
        using var store = new AccountStore(PathFor("b.dat"));
        var prior = await store.AddAsync("Prior", "https://avatar/p", "FAKE-COOKIE-prior-AAAA");
        await store.UpdateRobloxUserIdAsync(prior.Id, 12L);

        await store.ImportMergeAsync(new[] { Record("Incoming", 34L, "FAKE-COOKIE-incoming-BBBB") });

        // Prior account still readable across a cold start (DPAPI envelope intact).
        var list = await store.ListAsync();
        Assert.Equal(2, list.Count);
        Assert.Equal("FAKE-COOKIE-prior-AAAA", await store.RetrieveCookieAsync(prior.Id));
    }

    private static AccountExportRecord Record(string displayName, long userId, string cookie) =>
        new(
            DisplayName: displayName,
            AvatarUrl: $"https://avatar/{userId}.png",
            RobloxUserId: userId,
            Cookie: cookie,
            Tags: Array.Empty<string>(),
            FpsCap: null,
            CaptionColorHex: null,
            LocalName: null,
            IsMain: false,
            SortOrder: 0,
            IsSelected: true);
}
