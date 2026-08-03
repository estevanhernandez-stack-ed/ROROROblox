using System.Security.Cryptography;
using System.Text;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Crypto-layer coverage of <see cref="RobloxCompatSignature.Verify"/> — ported 1:1 from
/// 626-mod-launcher's <c>ManifestSignatureTests</c> (same ECDSA P-256/SHA-256 IEEE P1363 scheme).
/// Checker-level posture (how <see cref="RobloxCompatChecker"/> reacts to a verify failure —
/// degrade to no-update, never a crash, never a fallback to unverified content) lives in
/// <see cref="RobloxCompatCheckerTests"/>.
/// </summary>
public class RobloxCompatSignatureTests
{
    // Make an ephemeral P-256 keypair; return (spkiPublicKey, signer).
    private static (byte[] Spki, ECDsa Signer) NewKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfo(), ecdsa);
    }

    private static byte[] Sign(ECDsa signer, byte[] data)
        => signer.SignData(data, HashAlgorithmName.SHA256, RobloxCompatSignature.Format);

    [Fact]
    public void Valid_signature_over_the_bytes_verifies()
    {
        var (spki, signer) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"knownGoodVersionMin\":\"2.0.0.0\"}");
        var sig = Sign(signer, data);

        Assert.True(RobloxCompatSignature.Verify(spki, data, sig));
    }

    [Fact]
    public void Tampered_payload_fails()
    {
        var (spki, signer) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("{\"knownGoodVersionMin\":\"2.0.0.0\"}");
        var sig = Sign(signer, data);

        var tampered = Encoding.UTF8.GetBytes("{\"knownGoodVersionMin\":\"9.9.9.9\"}");
        Assert.False(RobloxCompatSignature.Verify(spki, tampered, sig));
    }

    [Fact]
    public void Tampered_signature_fails()
    {
        var (spki, signer) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("payload");
        var sig = Sign(signer, data);
        sig[0] ^= 0xFF; // flip a bit

        Assert.False(RobloxCompatSignature.Verify(spki, data, sig));
    }

    [Fact]
    public void Signature_from_a_different_key_fails()
    {
        var (_, signerA) = NewKeyPair();
        var (spkiB, _) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("payload");
        var sigFromA = Sign(signerA, data);

        Assert.False(RobloxCompatSignature.Verify(spkiB, data, sigFromA)); // wrong public key
    }

    [Theory]
    [InlineData(new byte[0])]              // empty signature
    [InlineData(new byte[] { 1, 2, 3 })]   // garbage signature
    public void Malformed_signature_returns_false_not_throws(byte[] badSig)
    {
        var (spki, _) = NewKeyPair();
        var data = Encoding.UTF8.GetBytes("payload");
        Assert.False(RobloxCompatSignature.Verify(spki, data, badSig));
    }

    [Fact]
    public void Garbage_public_key_returns_false_not_throws()
    {
        var data = Encoding.UTF8.GetBytes("payload");
        Assert.False(RobloxCompatSignature.Verify(new byte[] { 9, 9, 9 }, data, new byte[64]));
    }
}
