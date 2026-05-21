using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ROROROblox.Core.Transport;

/// <summary>
/// Pure-crypto implementation of the account transport bundle (spec §1). PBKDF2-HMAC-SHA256
/// (600k iterations) derives a 256-bit key from the passphrase + a per-bundle random salt;
/// AES-256-GCM AEAD encrypts the JSON payload with a per-bundle random nonce. Dependency-free —
/// only <see cref="System.Security.Cryptography"/>.
///
/// SECURITY-SENSITIVE. Invariants enforced here:
/// - No external crypto dependency; both primitives are BCL.
/// - Fresh 16-byte salt + 12-byte nonce per export (never reused) — two exports of identical
///   input produce different bytes.
/// - The iteration count is written INTO the header so a future bump can still open old bundles
///   (Import reads iters from the header, not the const).
/// - Fail-closed import: wrong passphrase, tampered data, or malformed header all throw the same
///   <see cref="AccountTransportException"/> with the same message — no oracle leak, no partial data.
/// - The derived key is zeroed after use; the passphrase / key / cookie / plaintext are never logged.
/// </summary>
public sealed class AccountTransportService : IAccountTransport
{
    /// <summary>PBKDF2-HMAC-SHA256 iteration count (OWASP 2023 floor for PBKDF2-SHA256).</summary>
    public const int Pbkdf2Iterations = 600_000;

    private const byte FormatVersion = 1;
    private const int KeyBytes = 32;   // AES-256
    private const int SaltBytes = 16;
    private const int NonceBytes = 12; // AES-GCM standard nonce
    private const int TagBytes = 16;   // AES-GCM full tag

    // magic | version(1) | iters(4) | salt(16) | nonce(12) | tag(16) | ciphertext
    private static readonly byte[] Magic = "RRRACCT\0"u8.ToArray();
    private const int HeaderLength = 8 + 1 + 4 + SaltBytes + NonceBytes + TagBytes; // 57

    private const int VersionOffset = 8;
    private const int ItersOffset = VersionOffset + 1; // 9
    private const int SaltOffset = ItersOffset + 4;    // 13
    private const int NonceOffset = SaltOffset + SaltBytes;  // 29
    private const int TagOffset = NonceOffset + NonceBytes;  // 41
    private const int CipherOffset = TagOffset + TagBytes;   // 57

    /// <summary>The one message the caller may show on any import failure. Deliberately ambiguous.</summary>
    private const string FailMessage =
        "Couldn't open this bundle — wrong passphrase or the file is damaged.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Compact (not indented) — the payload is encrypted bytes, not human-read.
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public byte[] Export(IReadOnlyList<AccountExportRecord> records, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(passphrase);

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(records, JsonOptions);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        byte[] key = DeriveKey(passphrase, salt, Pbkdf2Iterations);
        try
        {
            byte[] cipher = new byte[plaintext.Length];
            byte[] tag = new byte[TagBytes];
            using (var gcm = new AesGcm(key, TagBytes))
            {
                gcm.Encrypt(nonce, plaintext, cipher, tag);
            }

            var bundle = new byte[HeaderLength + cipher.Length];
            Magic.CopyTo(bundle, 0);
            bundle[VersionOffset] = FormatVersion;
            BinaryPrimitives.WriteInt32LittleEndian(bundle.AsSpan(ItersOffset, 4), Pbkdf2Iterations);
            salt.CopyTo(bundle, SaltOffset);
            nonce.CopyTo(bundle, NonceOffset);
            tag.CopyTo(bundle, TagOffset);
            cipher.CopyTo(bundle, CipherOffset);

            return bundle;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public IReadOnlyList<AccountExportRecord> Import(byte[] bundle, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(passphrase);

        // Header validation — any malformation throws the same ambiguous exception (no oracle leak).
        if (bundle.Length < HeaderLength)
        {
            throw new AccountTransportException(FailMessage);
        }
        if (!bundle.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new AccountTransportException(FailMessage);
        }
        if (bundle[VersionOffset] != FormatVersion)
        {
            throw new AccountTransportException(FailMessage);
        }

        int iters = BinaryPrimitives.ReadInt32LittleEndian(bundle.AsSpan(ItersOffset, 4));
        if (iters <= 0)
        {
            throw new AccountTransportException(FailMessage);
        }

        byte[] salt = bundle.AsSpan(SaltOffset, SaltBytes).ToArray();
        byte[] nonce = bundle.AsSpan(NonceOffset, NonceBytes).ToArray();
        byte[] tag = bundle.AsSpan(TagOffset, TagBytes).ToArray();
        byte[] cipher = bundle.AsSpan(CipherOffset).ToArray();

        byte[] key = DeriveKey(passphrase, salt, iters);
        byte[] plaintext = new byte[cipher.Length];
        try
        {
            using (var gcm = new AesGcm(key, TagBytes))
            {
                // Throws AuthenticationTagMismatchException (a CryptographicException) on wrong
                // passphrase OR tampered data — the tag is verified before any plaintext is exposed.
                gcm.Decrypt(nonce, cipher, tag, plaintext);
            }

            var records = JsonSerializer.Deserialize<List<AccountExportRecord>>(plaintext, JsonOptions);
            // A successfully-decrypted-but-empty/garbage payload is treated as damaged, never partial.
            return records ?? throw new AccountTransportException(FailMessage);
        }
        catch (CryptographicException ex)
        {
            // Wrong passphrase and tamper land here identically — same message, no leak.
            throw new AccountTransportException(FailMessage, ex);
        }
        catch (JsonException ex)
        {
            // Decrypt succeeded (tag verified) but payload didn't parse — still fail closed.
            throw new AccountTransportException(FailMessage, ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt, int iterations)
    {
        byte[] passphraseBytes = Encoding.UTF8.GetBytes(passphrase);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(passphraseBytes, salt, iterations, HashAlgorithmName.SHA256, KeyBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passphraseBytes);
        }
    }
}
