using System.Security.Cryptography;
using System.Text;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Ported 1:1 from 626-mod-launcher's <c>ManifestSigningKeyTests</c>. The production private key
/// lives only in this repo's CI (the <c>ROBLOXCOMPAT_SIGNING_KEY</c> secret), so these tests can't
/// make a genuine signature — the security-critical, testable property is REJECTION: the pinned
/// key must refuse a signature made by any other key. (Acceptance of a genuine signature is
/// covered generically in <see cref="RobloxCompatSignatureTests"/> with an in-test keypair, and at
/// the checker level in <see cref="RobloxCompatCheckerTests"/> via the injected pinnedPublicKey seam.)
/// </summary>
public class RobloxCompatSigningKeyTests
{
    [Fact]
    public void Pinned_key_imports_as_a_p256_verify_key()
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(RobloxCompatSigningKey.PublicKeySpki, out var read);

        Assert.Equal(RobloxCompatSigningKey.PublicKeySpki.Length, read); // consumed exactly, no trailing junk
        Assert.Equal(256, ecdsa.KeySize);
        Assert.Equal("1.2.840.10045.3.1.7", ecdsa.ExportParameters(false).Curve.Oid.Value); // NIST P-256
    }

    [Fact]
    public void Pinned_key_rejects_a_forged_signature()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = Encoding.UTF8.GetBytes("{\"knownGoodVersionMin\":\"2.0.0.0\"}");
        var forged = attacker.SignData(data, HashAlgorithmName.SHA256, RobloxCompatSignature.Format);

        Assert.False(RobloxCompatSignature.Verify(RobloxCompatSigningKey.PublicKeySpki, data, forged));
    }
}
