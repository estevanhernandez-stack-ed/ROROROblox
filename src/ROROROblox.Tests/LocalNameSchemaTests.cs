using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Item 1 (v1.3.x) — verifies the <c>LocalName</c> schema addition is forward-and-backward
/// compatible across the three records (FavoriteGame, SavedPrivateServer, Account) and their
/// store load paths. Three tests per record × three records = nine tests.
/// </summary>
/// <remarks>
/// <para>
/// Legacy-JSON-loads-as-null tests construct an on-disk fixture in the v1.2 shape
/// (no localName field) and load via the actual store — this exercises the serializer
/// config + (for SavedPrivateServer) the StoredServer migration shim + (for Account) the
/// DPAPI envelope. If any of those drops the new property silently, these tests fail loud.
/// </para>
/// <para>
/// Roundtrip-and-omit tests use direct JsonSerializer calls with the same options the stores
/// use (camelCase + WhenWritingNull) so a regression in the record's positional shape or in
/// the store's serializer options surfaces here, not in production.
/// </para>
/// </remarks>
public class LocalNameSchemaTests : IDisposable
{
    private readonly string _tempDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public LocalNameSchemaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-localname-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    // ---------- FavoriteGame ----------

    [Fact]
    public async Task FavoriteGame_LegacyJsonWithoutLocalName_LoadsAsNull()
    {
        var path = Path.Combine(_tempDir, "favorites.json");
        var legacy = """
            {
              "version": 1,
              "favorites": [
                {
                  "placeId": 920587237,
                  "universeId": 1818,
                  "name": "Adopt Me!",
                  "thumbnailUrl": "https://example/icon.png",
                  "isDefault": true,
                  "addedAt": "2026-04-01T00:00:00+00:00"
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, legacy);

        using var store = new FavoriteGameStore(path);
        var list = await store.ListAsync();

        Assert.Single(list);
        Assert.Null(list[0].LocalName);
        Assert.Equal("Adopt Me!", list[0].Name);
    }

    [Fact]
    public void FavoriteGame_RoundtripsCustomLocalName()
    {
        var record = new FavoriteGame(
            PlaceId: 100,
            UniverseId: 1,
            Name: "Original",
            ThumbnailUrl: "https://x",
            IsDefault: true,
            AddedAt: DateTimeOffset.UtcNow,
            LocalName: "My Adopt Me");

        var json = JsonSerializer.Serialize(record, JsonOptions);
        Assert.Contains("\"localName\":\"My Adopt Me\"", json);

        var roundtripped = JsonSerializer.Deserialize<FavoriteGame>(json, JsonOptions);
        Assert.NotNull(roundtripped);
        Assert.Equal("My Adopt Me", roundtripped!.LocalName);
        Assert.Equal("Original", roundtripped.Name);
    }

    [Fact]
    public void FavoriteGame_NullLocalNameOmittedFromJsonAndDeserializesAsNull()
    {
        var record = new FavoriteGame(
            PlaceId: 100,
            UniverseId: 1,
            Name: "Original",
            ThumbnailUrl: "https://x",
            IsDefault: true,
            AddedAt: DateTimeOffset.UtcNow,
            LocalName: null);

        var json = JsonSerializer.Serialize(record, JsonOptions);
        Assert.DoesNotContain("localName", json);

        var roundtripped = JsonSerializer.Deserialize<FavoriteGame>(json, JsonOptions);
        Assert.NotNull(roundtripped);
        Assert.Null(roundtripped!.LocalName);
    }

    // ---------- SavedPrivateServer ----------

    [Fact]
    public async Task SavedPrivateServer_LegacyJsonWithoutLocalName_LoadsAsNull()
    {
        var path = Path.Combine(_tempDir, "private-servers.json");
        var legacy = """
            {
              "version": 1,
              "servers": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "placeId": 100,
                  "code": "share-code",
                  "codeKind": 1,
                  "name": "Squad VIP",
                  "placeName": "Adopt Me",
                  "thumbnailUrl": "https://x/icon.png",
                  "addedAt": "2026-04-01T00:00:00+00:00",
                  "lastLaunchedAt": null
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(path, legacy);

        using var store = new PrivateServerStore(path);
        var list = await store.ListAsync();

        Assert.Single(list);
        Assert.Null(list[0].LocalName);
        Assert.Equal("Squad VIP", list[0].Name);
    }

    [Fact]
    public void SavedPrivateServer_RoundtripsCustomLocalName()
    {
        var record = new SavedPrivateServer(
            Id: Guid.NewGuid(),
            PlaceId: 100,
            Code: "share-code",
            CodeKind: PrivateServerCodeKind.LinkCode,
            Name: "Squad VIP",
            PlaceName: "Adopt Me",
            ThumbnailUrl: "https://x",
            AddedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            LocalName: "My Squad");

        var json = JsonSerializer.Serialize(record, JsonOptions);
        Assert.Contains("\"localName\":\"My Squad\"", json);

        var roundtripped = JsonSerializer.Deserialize<SavedPrivateServer>(json, JsonOptions);
        Assert.NotNull(roundtripped);
        Assert.Equal("My Squad", roundtripped!.LocalName);
        Assert.Equal("Squad VIP", roundtripped.Name);
    }

    [Fact]
    public void SavedPrivateServer_NullLocalNameOmittedFromJsonAndDeserializesAsNull()
    {
        var record = new SavedPrivateServer(
            Id: Guid.NewGuid(),
            PlaceId: 100,
            Code: "share-code",
            CodeKind: PrivateServerCodeKind.LinkCode,
            Name: "Squad VIP",
            PlaceName: "Adopt Me",
            ThumbnailUrl: "https://x",
            AddedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            LocalName: null);

        var json = JsonSerializer.Serialize(record, JsonOptions);
        Assert.DoesNotContain("localName", json);

        var roundtripped = JsonSerializer.Deserialize<SavedPrivateServer>(json, JsonOptions);
        Assert.NotNull(roundtripped);
        Assert.Null(roundtripped!.LocalName);
    }

    // ---------- Account (DPAPI envelope; legacy is a real on-disk fixture) ----------

    [Fact]
    public async Task Account_LegacyDpapiBlobWithoutLocalName_LoadsAsNull()
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
        Assert.Null(list[0].LocalName);
        Assert.Equal("TestUser", list[0].DisplayName);
    }

    [Fact]
    public void Account_RoundtripsCustomLocalName()
    {
        var record = new Account(
            Id: Guid.NewGuid(),
            DisplayName: "TestUser",
            AvatarUrl: "https://avatar",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            IsMain: false,
            SortOrder: 0,
            IsSelected: true,
            CaptionColorHex: null,
            FpsCap: null,
            LocalName: "Mr. Solo Dolo");

        var json = JsonSerializer.Serialize(record, JsonOptions);
        Assert.Contains("\"localName\":\"Mr. Solo Dolo\"", json);

        var roundtripped = JsonSerializer.Deserialize<Account>(json, JsonOptions);
        Assert.NotNull(roundtripped);
        Assert.Equal("Mr. Solo Dolo", roundtripped!.LocalName);
        Assert.Equal("TestUser", roundtripped.DisplayName);
    }

    [Fact]
    public void Account_NullLocalNameOmittedFromJsonAndDeserializesAsNull()
    {
        var record = new Account(
            Id: Guid.NewGuid(),
            DisplayName: "TestUser",
            AvatarUrl: "https://avatar",
            CreatedAt: DateTimeOffset.UtcNow,
            LastLaunchedAt: null,
            IsMain: false,
            SortOrder: 0,
            IsSelected: true,
            CaptionColorHex: null,
            FpsCap: null,
            LocalName: null);

        var json = JsonSerializer.Serialize(record, JsonOptions);
        Assert.DoesNotContain("localName", json);

        var roundtripped = JsonSerializer.Deserialize<Account>(json, JsonOptions);
        Assert.NotNull(roundtripped);
        Assert.Null(roundtripped!.LocalName);
    }
}
