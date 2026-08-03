using System.Security.Cryptography;

namespace ROROROblox.Core;

/// <summary>
/// Detached-signature verification for the <c>roblox-compat.json</c> feed. ECDSA over NIST P-256
/// with SHA-256 — the dependency-free, pure-BCL choice ported from 626-mod-launcher's manifest-
/// signing pattern (<c>ManifestSignature</c>; Ed25519 is not first-class in
/// <c>System.Security.Cryptography</c> until .NET 11). The signer (CI) holds the private key; the
/// app pins only the public key (SubjectPublicKeyInfo) and verifies. Signs/verifies the EXACT
/// bytes fetched off the wire — the caller must pass the literal response body, never a
/// re-serialized copy, or whitespace/key-order drift breaks verification.
/// </summary>
public static class RobloxCompatSignature
{
    // Both sides MUST agree on this format. P1363 gives fixed 64-byte signatures for P-256.
    public const DSASignatureFormat Format = DSASignatureFormat.IeeeP1363FixedFieldConcatenation;

    /// <summary>
    /// True iff <paramref name="signature"/> is a valid P-256/SHA-256 signature over
    /// <paramref name="data"/> by the private key matching <paramref name="subjectPublicKeyInfo"/>.
    /// Never throws on malformed key/signature input — returns false.
    /// </summary>
    public static bool Verify(
        ReadOnlySpan<byte> subjectPublicKeyInfo,
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte> signature)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return ecdsa.VerifyData(data, signature, HashAlgorithmName.SHA256, Format);
        }
        catch (CryptographicException)
        {
            // malformed SPKI, wrong-length signature, etc. — a verification failure, not a crash
            return false;
        }
    }
}
