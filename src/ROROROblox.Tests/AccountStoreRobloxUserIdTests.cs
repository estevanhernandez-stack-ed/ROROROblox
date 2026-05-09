using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Cycle-5 round-trip + forward-compat tests for the new <see cref="Account.RobloxUserId"/>
/// field. Real-DPAPI (CurrentUser scope) per-test temp directory, same pattern as
/// <see cref="AccountStoreTests"/>. Spec §4.1, §4.5, §7.
/// </summary>
public class AccountStoreRobloxUserIdTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public AccountStoreRobloxUserIdTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ROROROblox-test-userid-{Guid.NewGuid():N}");
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
    public async Task NewAccount_HasNullRobloxUserId_ByDefault()
    {
        // AddAsync doesn't take a userId — it's resolved later via cookie + GetUserProfileAsync,
        // OR seeded by the OnCookieCapturedAsync flow. The fresh-add path leaves it null.
        using var store = new AccountStore(_filePath);

        var account = await store.AddAsync("TestUser", "https://avatar.example/img.png", "fake-cookie-1");

        Assert.Null(account.RobloxUserId);

        var list = await store.ListAsync();
        Assert.Single(list);
        Assert.Null(list[0].RobloxUserId);
    }

    [Fact]
    public async Task ExistingV1_3_1_0_Blob_DecodesWithNullRobloxUserId()
    {
        // Forward-compat regression guard: a blob written by v1.3.1.0 (which has no
        // RobloxUserId field on StoredAccount) MUST decode cleanly with the current code,
        // landing the new field as null. The whole "no migration script" promise (spec §4.5)
        // rests on this being green. If a future change to Account ever breaks this, ship is
        // blocked — every existing user would lose their accounts.dat on upgrade.
        var legacyAccountJson = """
            {
              "version": 1,
              "accounts": [
                {
                  "id": "11111111-2222-3333-4444-555555555555",
                  "displayName": "LegacyUser",
                  "avatarUrl": "https://avatar.example/legacy.png",
                  "cookie": "legacy-cookie-blob",
                  "createdAt": "2026-04-01T12:00:00+00:00",
                  "lastLaunchedAt": null,
                  "isMain": true,
                  "sortOrder": 0,
                  "isSelected": true
                }
              ]
            }
            """;
        var encrypted = ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(legacyAccountJson),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_filePath, encrypted);

        using var store = new AccountStore(_filePath);
        var list = await store.ListAsync();

        Assert.Single(list);
        Assert.Equal("LegacyUser", list[0].DisplayName);
        Assert.Null(list[0].RobloxUserId); // the load-bearing assertion
        Assert.Null(list[0].LocalName); // belt-and-suspenders — confirms other optional fields still default
        Assert.True(list[0].IsMain);
    }

    [Fact]
    public async Task RobloxUserId_RoundTripsThroughDpapiBlob()
    {
        // Even though AddAsync doesn't seed RobloxUserId today, the StoredAccount layer must
        // round-trip the field correctly when set via UpdateRobloxUserIdAsync (item 2). This
        // test pre-validates the serialization contract by writing a blob with a userId and
        // reading it back — sidesteps the API surface to test pure JSON+DPAPI round-trip.
        // Once item 2 lands, the same shape is reachable via the public API.
        var blobJson = """
            {
              "version": 1,
              "accounts": [
                {
                  "id": "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
                  "displayName": "RoundTripUser",
                  "avatarUrl": "https://avatar.example/rt.png",
                  "cookie": "rt-cookie",
                  "createdAt": "2026-05-08T20:00:00+00:00",
                  "lastLaunchedAt": null,
                  "isMain": false,
                  "sortOrder": 1,
                  "isSelected": true,
                  "robloxUserId": 1234567890
                }
              ]
            }
            """;
        var encrypted = ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(blobJson),
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_filePath, encrypted);

        using var store = new AccountStore(_filePath);
        var list = await store.ListAsync();

        Assert.Single(list);
        Assert.Equal(1234567890L, list[0].RobloxUserId);
    }
}
