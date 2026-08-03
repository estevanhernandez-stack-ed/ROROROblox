namespace ROROROblox.Core;

/// <summary>
/// The pinned public key for verifying the <c>roblox-compat.json</c> feed's detached signature
/// (ECDSA P-256 / SHA-256). This is the trust anchor <see cref="RobloxCompatChecker"/> verifies
/// against before trusting anything the feed returns — see <see cref="RobloxCompatSignature.Verify"/>.
///
/// A SEPARATE keypair from 626-mod-launcher's manifest-signing key (one key per trust surface —
/// this feed and the mod-launcher's games manifest are unrelated products and unrelated blast
/// radii). The matching private key lives ONLY in this repo's CI as the <c>ROBLOXCOMPAT_SIGNING_KEY</c>
/// GitHub Actions secret; it never appears in source. A public key is safe to commit. Rotation =
/// mint a new keypair and ship a release that re-pins <see cref="PublicKeySpki"/> (the key is pinned
/// in the binary, so rotation is a release — no rotation machinery by design, matching the
/// mod-launcher precedent this pattern is ported from).
/// </summary>
public static class RobloxCompatSigningKey
{
    // SubjectPublicKeyInfo (DER), base64. Generated 2026-08-03 — ECDSA P-256 (secp256r1).
    // Validated on pin: imports as a 256-bit key on curve 1.2.840.10045.3.1.7 (see RobloxCompatSigningKeyTests).
    private const string PublicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEBf27zCsPITqy+FDuqAywt9b6o9js6Y29" +
        "d35yl4b9HiViBdAP8xKvMbJKOYT71VGpFVZrc9Shu84UlCLyD+2KGQ==";

    /// <summary>
    /// The pinned public key as SubjectPublicKeyInfo bytes. Pass to
    /// <see cref="RobloxCompatSignature.Verify"/> as the trust anchor when verifying the feed.
    /// </summary>
    public static byte[] PublicKeySpki { get; } = Convert.FromBase64String(PublicKeySpkiBase64);
}
