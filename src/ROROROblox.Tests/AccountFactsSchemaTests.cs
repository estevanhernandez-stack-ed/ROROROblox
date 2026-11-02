using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// v1.36 item 3: the <c>Facts</c>, <c>Note</c> and <c>PanelExpanded</c> additions to
/// <see cref="Account"/> and the DPAPI blob are forward-and-backward compatible. A copy of
/// <see cref="LocalNameSchemaTests"/>'s Account trio, with the store's three setters added.
/// Fixtures use fake cookies only.
/// </summary>
public class AccountFactsSchemaTests : IDisposable
{
    private readonly string _tempDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AccountFactsSchemaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-facts-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private static Account Base(AccountFacts? facts = null, string? note = null, bool? panelExpanded = null) => new(
        Id: Guid.NewGuid(),
        DisplayName: "TestUser",
        AvatarUrl: "https://avatar",
        CreatedAt: DateTimeOffset.UtcNow,
        LastLaunchedAt: null,
        Facts: facts,
        Note: note,
        PanelExpanded: panelExpanded);

    // ---------- Account record: roundtrip + null omitted ----------

    [Fact]
    public void Account_RoundtripsFactsNoteAndPanelExpanded()
    {
        var fetched = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var facts = new AccountFacts(Robux: 1240, Membership: "plus", FriendsOnline: 3, FetchedAtUtc: fetched, LastError: "403");
        var json = JsonSerializer.Serialize(Base(facts, "clan farm alt", true), JsonOptions);

        Assert.Contains("\"facts\":", json);
        Assert.Contains("\"robux\":1240", json);
        Assert.Contains("\"note\":\"clan farm alt\"", json);
        Assert.Contains("\"panelExpanded\":true", json);

        var back = JsonSerializer.Deserialize<Account>(json, JsonOptions);
        Assert.NotNull(back);
        Assert.Equal(facts, back!.Facts);
        Assert.Equal("clan farm alt", back.Note);
        Assert.True(back.PanelExpanded);
    }

    [Fact]
    public void Account_NullFactsNoteAndPanelExpanded_AreOmittedAndDeserializeAsNull()
    {
        var json = JsonSerializer.Serialize(Base(), JsonOptions);

        Assert.DoesNotContain("facts", json);
        Assert.DoesNotContain("note", json);
        Assert.DoesNotContain("panelExpanded", json);

        var back = JsonSerializer.Deserialize<Account>(json, JsonOptions);
        Assert.NotNull(back);
        Assert.Null(back!.Facts);
        Assert.Null(back.Note);
        Assert.Null(back.PanelExpanded);
    }

    [Fact]
    public void AccountFacts_HoldsNoCookieMember()
    {
        // By construction: the record has no member that could carry a credential.
        var names = typeof(AccountFacts).GetProperties().Select(p => p.Name).ToList();
        Assert.Equal(new[] { "Robux", "Membership", "FriendsOnline", "FetchedAtUtc", "LastError" }, names);
    }

    // ---------- The DPAPI blob: a v1.35 file loads all three as null ----------

    [Fact]
    public async Task LegacyDpapiBlob_FromV135_LoadsFactsNoteAndPanelExpandedAsNull()
    {
        var path = Path.Combine(_tempDir, "accounts.dat");
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
                  "isSelected": true,
                  "localName": "Main",
                  "robloxUserId": 42,
                  "joinViaFriend": false,
                  "autoRejoin": true
                }
              ]
            }
            """;
        await File.WriteAllBytesAsync(path, ProtectedData.Protect(
            Encoding.UTF8.GetBytes(legacy), optionalEntropy: null, DataProtectionScope.CurrentUser));

        using var store = new AccountStore(path);
        var account = Assert.Single(await store.ListAsync());

        Assert.Null(account.Facts);
        Assert.Null(account.Note);
        Assert.Null(account.PanelExpanded);
        Assert.Equal("Main", account.LocalName);
        Assert.True(account.AutoRejoin);
    }

    [Fact]
    public async Task Store_PersistsFactsNoteAndPanelExpanded_AcrossAReload()
    {
        var path = Path.Combine(_tempDir, "accounts.dat");
        var fetched = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var facts = new AccountFacts(1240, "none", 3, fetched, null);
        Guid id;
        using (var store = new AccountStore(path))
        {
            id = (await store.AddAsync("Alpha", "https://a", "FAKE-COOKIE-schema-AAAA")).Id;
            await store.SetFactsAsync(id, facts);
            await store.UpdateNoteAsync(id, "  hello  ");
            await store.SetPanelExpandedAsync(id, true);
        }

        using var reloaded = new AccountStore(path);
        var account = Assert.Single(await reloaded.ListAsync());
        Assert.Equal(facts, account.Facts);
        Assert.Equal("hello", account.Note);
        Assert.True(account.PanelExpanded);
        // The cookie rides the same blob and is untouched by the three setters.
        Assert.Equal("FAKE-COOKIE-schema-AAAA", await reloaded.RetrieveCookieAsync(id));
    }

    // ---------- The three setters: the SetJoinViaFriendAsync family ----------

    [Fact]
    public async Task Setters_OnAnUnknownId_AreSilentNoOpsAndWriteNothing()
    {
        var path = Path.Combine(_tempDir, "accounts.dat");
        using var store = new AccountStore(path);
        var known = await store.AddAsync("Alpha", "https://a", "FAKE-COOKIE-schema-BBBB");
        var before = await File.ReadAllBytesAsync(path);
        var ghost = Guid.NewGuid();

        await store.SetFactsAsync(ghost, new AccountFacts(1, null, null, DateTimeOffset.UtcNow, null));
        await store.UpdateNoteAsync(ghost, "x");
        await store.SetPanelExpandedAsync(ghost, true);

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        var account = Assert.Single(await store.ListAsync());
        Assert.Equal(known.Id, account.Id);
        Assert.Null(account.Facts);
        Assert.Null(account.Note);
        Assert.Null(account.PanelExpanded);
    }

    [Fact]
    public async Task UpdateNote_TrimsBlankBecomesNull_AndCapsLength()
    {
        using var store = new AccountStore(Path.Combine(_tempDir, "accounts.dat"));
        var id = (await store.AddAsync("Alpha", "https://a", "FAKE-COOKIE-schema-CCCC")).Id;

        await store.UpdateNoteAsync(id, "  kept  ");
        Assert.Equal("kept", Assert.Single(await store.ListAsync()).Note);

        await store.UpdateNoteAsync(id, new string('y', Account.MaxNoteLength + 25));
        Assert.Equal(Account.MaxNoteLength, Assert.Single(await store.ListAsync()).Note!.Length);

        await store.UpdateNoteAsync(id, "   ");
        Assert.Null(Assert.Single(await store.ListAsync()).Note);
    }

    [Fact]
    public async Task SetFacts_WithAnUnchangedValue_SkipsTheDpapiWrite()
    {
        var path = Path.Combine(_tempDir, "accounts.dat");
        using var store = new AccountStore(path);
        var id = (await store.AddAsync("Alpha", "https://a", "FAKE-COOKIE-schema-DDDD")).Id;
        var facts = new AccountFacts(5, "none", 1, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), null);
        await store.SetFactsAsync(id, facts);
        var bytes = await File.ReadAllBytesAsync(path);

        await store.SetFactsAsync(id, facts with { });

        // DPAPI output is salted, so a rewrite would change the bytes even for identical plaintext.
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }
}
