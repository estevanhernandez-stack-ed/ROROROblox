using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Streamer mode (2026-07-10) — verifies the <c>StreamerName</c>/<c>StreamerAvatarId</c> schema
/// addition on <see cref="Account"/> is forward-and-backward compatible. Mirrors the Account
/// section of <see cref="LocalNameSchemaTests"/>:
/// (1) a legacy DPAPI blob WITHOUT the two new fields loads with both == null (no throw), against
///     a real on-disk fixture through the actual store (exercises serializer + DPAPI envelope),
/// (2) custom values round-trip through the store's serializer options,
/// (3) null values are omitted from JSON and deserialize back as null.
/// </summary>
public class StreamerIdentitySchemaTests : IDisposable
{
    private readonly string _tempDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public StreamerIdentitySchemaTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-streamer-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Account_LegacyDpapiBlobWithoutStreamerFields_LoadsAsNull()
    {
        var path = Path.Combine(_tempDir, "accounts.dat");
        // v1.x shape predating streamer mode — no streamerName / streamerAvatarId fields.
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
        Assert.Null(list[0].StreamerName);
        Assert.Null(list[0].StreamerAvatarId);
        Assert.Equal("TestUser", list[0].DisplayName);
    }

    [Fact]
    public void Account_RoundtripsCustomStreamerFields()
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
            LocalName: null,
            StreamerName: "CaptainNoodle",
            StreamerAvatarId: "noodle");

        var json = JsonSerializer.Serialize(record, JsonOptions);
        Assert.Contains("\"streamerName\":\"CaptainNoodle\"", json);
        Assert.Contains("\"streamerAvatarId\":\"noodle\"", json);

        var roundtripped = JsonSerializer.Deserialize<Account>(json, JsonOptions);
        Assert.NotNull(roundtripped);
        Assert.Equal("CaptainNoodle", roundtripped!.StreamerName);
        Assert.Equal("noodle", roundtripped.StreamerAvatarId);
        Assert.Equal("TestUser", roundtripped.DisplayName);
    }

    [Fact]
    public void Account_NullStreamerFieldsOmittedFromJsonAndDeserializeAsNull()
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
            LocalName: null,
            StreamerName: null,
            StreamerAvatarId: null);

        var json = JsonSerializer.Serialize(record, JsonOptions);
        Assert.DoesNotContain("streamerName", json);
        Assert.DoesNotContain("streamerAvatarId", json);

        var roundtripped = JsonSerializer.Deserialize<Account>(json, JsonOptions);
        Assert.NotNull(roundtripped);
        Assert.Null(roundtripped!.StreamerName);
        Assert.Null(roundtripped.StreamerAvatarId);
    }
}
