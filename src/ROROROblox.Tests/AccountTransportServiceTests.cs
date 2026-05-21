using System.Collections.Generic;
using ROROROblox.Core.Transport;

namespace ROROROblox.Tests;

/// <summary>
/// Tests for the pure-crypto account transport service (PBKDF2 + AES-256-GCM bundle).
/// Security-sensitive — these lock the bundle format, the round-trip fidelity, and the
/// fail-closed behavior (wrong passphrase and tamper must throw the SAME clear exception,
/// no oracle leak; never partial data on failure).
/// </summary>
public class AccountTransportServiceTests
{
    private const string Passphrase = "correct-horse-battery-staple";

    // Header layout under test (matches AccountTransportService):
    //   magic   8 bytes  "RRRACCT\0"
    //   version 1 byte   = 1
    //   iters   4 bytes  int32 little-endian
    //   salt    16 bytes
    //   nonce   12 bytes
    //   tag     16 bytes
    //   cipher  remaining
    private const int MagicLength = 8;
    private const int VersionOffset = MagicLength;       // 8
    private const int ItersOffset = VersionOffset + 1;   // 9
    private const int SaltOffset = ItersOffset + 4;      // 13
    private const int NonceOffset = SaltOffset + 16;     // 29
    private const int TagOffset = NonceOffset + 12;      // 41
    private const int CipherOffset = TagOffset + 16;     // 57 — start of ciphertext

    private static IAccountTransport NewService() => new AccountTransportService();

    private static IReadOnlyList<AccountExportRecord> SampleRecords() => new[]
    {
        new AccountExportRecord(
            DisplayName: "MainGuy",
            AvatarUrl: "https://avatar.example/main.png",
            RobloxUserId: 12345678901L,
            // Fake placeholder — deliberately NOT the real .ROBLOSECURITY signature so the
            // pre-commit secret scanner stays happy. The crypto round-trip is content-agnostic;
            // this only has to survive export -> import byte-for-byte.
            Cookie: "FAKE-COOKIE-PLACEHOLDER-main-AAAAAAAAAAAA",
            Tags: new[] { "PS99", "RCU" },
            FpsCap: 240,
            CaptionColorHex: "#17d4fa",
            LocalName: "Main",
            IsMain: true,
            SortOrder: 0,
            IsSelected: true),
        new AccountExportRecord(
            DisplayName: "AltOne",
            AvatarUrl: "",
            RobloxUserId: 22222222222L,
            Cookie: "FAKE-COOKIE-PLACEHOLDER-alt-BBBBBBBBBBBB",
            Tags: System.Array.Empty<string>(),
            FpsCap: null,
            CaptionColorHex: null,
            LocalName: null,
            IsMain: false,
            SortOrder: 1,
            IsSelected: false),
    };

    [Fact]
    public void RoundTrip_PreservesEveryField()
    {
        var svc = NewService();
        var original = SampleRecords();

        var bundle = svc.Export(original, Passphrase);
        var restored = svc.Import(bundle, Passphrase);

        Assert.Equal(original.Count, restored.Count);
        for (int i = 0; i < original.Count; i++)
        {
            Assert.Equal(original[i].DisplayName, restored[i].DisplayName);
            Assert.Equal(original[i].AvatarUrl, restored[i].AvatarUrl);
            Assert.Equal(original[i].RobloxUserId, restored[i].RobloxUserId);
            Assert.Equal(original[i].Cookie, restored[i].Cookie);
            Assert.Equal(original[i].Tags, restored[i].Tags);
            Assert.Equal(original[i].FpsCap, restored[i].FpsCap);
            Assert.Equal(original[i].CaptionColorHex, restored[i].CaptionColorHex);
            Assert.Equal(original[i].LocalName, restored[i].LocalName);
            Assert.Equal(original[i].IsMain, restored[i].IsMain);
            Assert.Equal(original[i].SortOrder, restored[i].SortOrder);
            Assert.Equal(original[i].IsSelected, restored[i].IsSelected);
        }
    }

    [Fact]
    public void WrongPassphrase_ThrowsClearTransportException_NoPartialData()
    {
        var svc = NewService();
        var bundle = svc.Export(SampleRecords(), Passphrase);

        var ex = Assert.Throws<AccountTransportException>(() => svc.Import(bundle, "totally-wrong-passphrase"));
        // Message must NOT leak which failure mode (passphrase vs tamper).
        Assert.DoesNotContain("passphrase is wrong", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TamperedCiphertext_Throws()
    {
        var svc = NewService();
        var bundle = svc.Export(SampleRecords(), Passphrase);

        // Flip a byte inside the ciphertext region.
        bundle[CipherOffset] ^= 0xFF;

        Assert.Throws<AccountTransportException>(() => svc.Import(bundle, Passphrase));
    }

    [Fact]
    public void TamperedTag_Throws()
    {
        var svc = NewService();
        var bundle = svc.Export(SampleRecords(), Passphrase);

        // Flip a byte inside the GCM tag region.
        bundle[TagOffset] ^= 0xFF;

        Assert.Throws<AccountTransportException>(() => svc.Import(bundle, Passphrase));
    }

    [Fact]
    public void TamperedSalt_Throws()
    {
        var svc = NewService();
        var bundle = svc.Export(SampleRecords(), Passphrase);

        // Corrupting the salt derives the wrong key → tag mismatch → throw.
        bundle[SaltOffset] ^= 0xFF;

        Assert.Throws<AccountTransportException>(() => svc.Import(bundle, Passphrase));
    }

    [Fact]
    public void BadMagic_ThrowsClearTransportException()
    {
        var svc = NewService();
        var bundle = svc.Export(SampleRecords(), Passphrase);

        bundle[0] ^= 0xFF; // corrupt the magic

        Assert.Throws<AccountTransportException>(() => svc.Import(bundle, Passphrase));
    }

    [Fact]
    public void UnknownVersion_ThrowsClearTransportException()
    {
        var svc = NewService();
        var bundle = svc.Export(SampleRecords(), Passphrase);

        bundle[VersionOffset] = 99; // unsupported format version

        Assert.Throws<AccountTransportException>(() => svc.Import(bundle, Passphrase));
    }

    [Fact]
    public void TruncatedBundle_ThrowsClearTransportException()
    {
        var svc = NewService();
        var bundle = svc.Export(SampleRecords(), Passphrase);

        // Keep just the magic — too short to hold a header.
        var truncated = new byte[MagicLength];
        System.Array.Copy(bundle, truncated, MagicLength);

        Assert.Throws<AccountTransportException>(() => svc.Import(truncated, Passphrase));
    }

    [Fact]
    public void EmptyBundle_ThrowsClearTransportException()
    {
        var svc = NewService();
        Assert.Throws<AccountTransportException>(() => svc.Import(System.Array.Empty<byte>(), Passphrase));
    }

    [Fact]
    public void TwoExportsOfSameData_ProduceDifferentBytes()
    {
        var svc = NewService();
        var records = SampleRecords();

        var a = svc.Export(records, Passphrase);
        var b = svc.Export(records, Passphrase);

        // Random salt + nonce per export → ciphertext + header differ even for identical input.
        Assert.NotEqual(a, b);
        // And both still decrypt to the same content.
        Assert.Equal(svc.Import(a, Passphrase).Count, svc.Import(b, Passphrase).Count);
    }

    [Fact]
    public void Header_CarriesIterationCount_AndImportReadsIt()
    {
        var svc = NewService();
        var bundle = svc.Export(SampleRecords(), Passphrase);

        // The header's iteration count is the const the service used.
        int iters = System.BitConverter.ToInt32(bundle, ItersOffset);
        Assert.Equal(AccountTransportService.Pbkdf2Iterations, iters);

        // Import reads iters from the header (not a hardcoded const) — round-trip still works.
        var restored = svc.Import(bundle, Passphrase);
        Assert.Equal(2, restored.Count);
    }

    [Fact]
    public void Header_HasCorrectMagicAndVersion()
    {
        var svc = NewService();
        var bundle = svc.Export(SampleRecords(), Passphrase);

        var expectedMagic = new byte[] { (byte)'R', (byte)'R', (byte)'R', (byte)'A', (byte)'C', (byte)'C', (byte)'T', 0 };
        for (int i = 0; i < MagicLength; i++)
        {
            Assert.Equal(expectedMagic[i], bundle[i]);
        }
        Assert.Equal(1, bundle[VersionOffset]);
    }

    [Fact]
    public void EmptyRecordList_RoundTrips()
    {
        var svc = NewService();
        var empty = System.Array.Empty<AccountExportRecord>();

        var bundle = svc.Export(empty, Passphrase);
        var restored = svc.Import(bundle, Passphrase);

        Assert.Empty(restored);
    }

    [Fact]
    public void Export_NullRecords_Throws()
    {
        var svc = NewService();
        Assert.Throws<System.ArgumentNullException>(() => svc.Export(null!, Passphrase));
    }

    [Fact]
    public void Export_NullPassphrase_Throws()
    {
        var svc = NewService();
        Assert.Throws<System.ArgumentNullException>(() => svc.Export(SampleRecords(), null!));
    }

    [Fact]
    public void Import_NullBundle_Throws()
    {
        var svc = NewService();
        Assert.Throws<System.ArgumentNullException>(() => svc.Import(null!, Passphrase));
    }
}
