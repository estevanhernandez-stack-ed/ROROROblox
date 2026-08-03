using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ROROROblox.Core;

namespace ROROROblox.Tests;

/// <summary>
/// Coverage of <see cref="RobloxCompatChecker.ResolveMutexNameAsync"/> — the config-driven
/// singleton-mutex-name resolver (spec item #1). Three-tier fallback: valid remote config ->
/// last-known-good cache -> hardcoded <see cref="MutexHolder.DefaultMutexName"/>. Degrade-safe:
/// ANY failure resolves to a usable name and NEVER throws, so a broken roblox-compat.json can
/// never brick multi-instance. HTTP is stubbed via <see cref="StubHttpHandler"/>; the
/// last-known-good cache is an injected seam (no disk).
///
/// <para>The feed is signed (ECDSA P-256/SHA-256, ported from 626-mod-launcher's manifest-signing
/// pattern). Every "valid remote config" test here signs the config bytes with an ephemeral in-test
/// keypair and injects the matching public key via <see cref="RobloxCompatChecker"/>'s
/// <c>pinnedPublicKey</c> seam — mirroring 626-mod-launcher's <c>RemoteManifestCacheTests</c>. The
/// production private key lives only in CI and is never needed here. Crypto-layer verify/reject
/// coverage (valid signature, tampered payload, tampered signature, wrong key, malformed input)
/// lives in <see cref="RobloxCompatSignatureTests"/>; this file covers the CHECKER's posture around
/// the verify gate — a tampered payload or a missing/invalid signature degrades exactly like a
/// network failure (no update available), never a crash, never a fallback to unverified content.</para>
/// </summary>
public class RobloxCompatCheckerTests
{
    private static HttpResponseMessage Bytes(HttpStatusCode status, byte[] body) =>
        new(status) { Content = new ByteArrayContent(body) };

    // Ephemeral P-256 test keypair — NOT the production key (which lives only in CI as the
    // ROBLOXCOMPAT_SIGNING_KEY secret). Mirrors 626-mod-launcher's ManifestSignatureTests /
    // RemoteManifestCacheTests pattern.
    private static (byte[] Spki, ECDsa Signer) NewKeyPair()
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ecdsa.ExportSubjectPublicKeyInfo(), ecdsa);
    }

    private static byte[] Sign(ECDsa signer, byte[] data) =>
        signer.SignData(data, HashAlgorithmName.SHA256, RobloxCompatSignature.Format);

    // Builds valid roblox-compat.json bytes (camelCase) via the serializer — the exact bytes the
    // signature covers, matching what FetchConfigAsync fetches and verifies before deserializing.
    private static byte[] ConfigBytes(string mutexName) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            knownGoodVersionMin = "2.0.0.0",
            knownGoodVersionMax = "2.999.0.0",
            mutexName,
            generatedAt = "2026-05-28T00:00:00Z",
        });

    private static RobloxCompatChecker Checker(
        StubHttpHandler stub,
        byte[] publicKey,
        Func<string?>? readLkg = null,
        Action<string>? writeLkg = null) =>
        new(new HttpClient(stub), readLkg ?? (() => null), writeLkg ?? (_ => { }), pinnedPublicKey: publicKey);

    // Enqueues a validly-signed config for ONE fetch: FetchConfigAsync issues two sequential GETs
    // (config bytes, then the .sig sibling), so a "happy path" fetch needs two queued responses.
    private static void EnqueueSignedConfig(StubHttpHandler stub, ECDsa signer, byte[] configBytes)
    {
        stub.EnqueueResponse(Bytes(HttpStatusCode.OK, configBytes));
        stub.EnqueueResponse(Bytes(HttpStatusCode.OK, Sign(signer, configBytes)));
    }

    [Fact]
    public async Task ResolveMutexNameAsync_ReturnsRemoteConfig_WhenConfigNameValid()
    {
        var (spki, signer) = NewKeyPair();
        var stub = new StubHttpHandler();
        EnqueueSignedConfig(stub, signer, ConfigBytes(@"Local\ROBLOX_singletonEvent"));
        var checker = Checker(stub, spki);

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_singletonEvent", name);
        Assert.Equal(MutexNameSource.RemoteConfig, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_PersistsValidRemoteNameToLastKnownGood()
    {
        var (spki, signer) = NewKeyPair();
        var stub = new StubHttpHandler();
        EnqueueSignedConfig(stub, signer, ConfigBytes(@"Local\ROBLOX_renamed_2027"));
        string? persisted = null;
        var checker = Checker(stub, spki, writeLkg: v => persisted = v);

        await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_renamed_2027", persisted);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_FallsBackToLastKnownGood_WhenFetchFails()
    {
        var (spki, _) = NewKeyPair();
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(_ => throw new HttpRequestException("offline"));
        var checker = Checker(stub, spki, readLkg: () => @"Local\ROBLOX_renamed_2027");

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_renamed_2027", name);
        Assert.Equal(MutexNameSource.LastKnownGood, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_PrefersLastKnownGood_WhenRemoteNameInvalid()
    {
        var (spki, signer) = NewKeyPair();
        var stub = new StubHttpHandler();
        EnqueueSignedConfig(stub, signer, ConfigBytes(""));   // empty -> invalid remote name
        var checker = Checker(stub, spki, readLkg: () => @"Local\ROBLOX_renamed_2027");

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_renamed_2027", name);
        Assert.Equal(MutexNameSource.LastKnownGood, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_FallsBackToDefault_WhenFetchFailsAndNoCache()
    {
        var (spki, _) = NewKeyPair();
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var checker = Checker(stub, spki, readLkg: () => null);

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(MutexHolder.DefaultMutexName, name);
        Assert.Equal(MutexNameSource.Default, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_FallsBackToDefault_WhenCacheIsAlsoInvalid()
    {
        var (spki, _) = NewKeyPair();
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(_ => throw new HttpRequestException("offline"));
        var checker = Checker(stub, spki, readLkg: () => "   ");   // garbage cache

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(MutexHolder.DefaultMutexName, name);
        Assert.Equal(MutexNameSource.Default, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_DoesNotPersist_WhenRemoteNameInvalid()
    {
        var (spki, signer) = NewKeyPair();
        var stub = new StubHttpHandler();
        EnqueueSignedConfig(stub, signer, ConfigBytes(""));
        var persisted = false;
        var checker = Checker(stub, spki, readLkg: () => null, writeLkg: _ => persisted = true);

        await checker.ResolveMutexNameAsync();

        Assert.False(persisted);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_NeverThrows_WhenPersistThrows()
    {
        var (spki, signer) = NewKeyPair();
        var stub = new StubHttpHandler();
        EnqueueSignedConfig(stub, signer, ConfigBytes(@"Local\ROBLOX_ok"));
        var checker = Checker(stub, spki, writeLkg: _ => throw new IOException("disk full"));

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_ok", name);
        Assert.Equal(MutexNameSource.RemoteConfig, source);   // persist failure is swallowed
    }

    [Fact]
    public async Task ResolveMutexNameAsync_NeverThrows_WhenCacheReadThrows()
    {
        var (spki, _) = NewKeyPair();
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(_ => throw new HttpRequestException("offline"));
        var checker = Checker(stub, spki, readLkg: () => throw new IOException("disk read failed"));

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(MutexHolder.DefaultMutexName, name);
        Assert.Equal(MutexNameSource.Default, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_HitsCompatConfigEndpoint_WithGet()
    {
        var (spki, signer) = NewKeyPair();
        var stub = new StubHttpHandler();
        EnqueueSignedConfig(stub, signer, ConfigBytes(@"Local\ROBLOX_singletonEvent"));
        var checker = Checker(stub, spki);

        await checker.ResolveMutexNameAsync();

        Assert.Equal(2, stub.Requests.Count);   // config bytes, then the .sig sibling
        Assert.All(stub.Requests, req => Assert.Equal(HttpMethod.Get, req.Method));
        Assert.Contains("roblox-compat.json", stub.Requests[0].RequestUri?.ToString());
        Assert.Contains("roblox-compat.json.sig", stub.Requests[1].RequestUri?.ToString());
    }

    // --- Signature-verify posture at the checker level (case a: valid signature verifies is
    // exercised by every "...RemoteConfig..." test above via EnqueueSignedConfig). ---

    [Fact]
    public async Task ResolveMutexNameAsync_TreatsTamperedPayload_AsNoUpdate_FallsBackToLastKnownGood()
    {
        // Case (b): a tampered payload — signature was valid for the ORIGINAL bytes, but the bytes
        // fetched off the wire were altered after signing. Verify runs BEFORE deserialize, so this
        // must degrade exactly like a network failure, never partially trust the tampered content.
        var (spki, signer) = NewKeyPair();
        var stub = new StubHttpHandler();
        var config = ConfigBytes(@"Local\ROBLOX_singletonEvent");
        var sig = Sign(signer, config);
        config[config.Length - 3] ^= 0xFF; // tamper AFTER signing
        stub.EnqueueResponse(Bytes(HttpStatusCode.OK, config));
        stub.EnqueueResponse(Bytes(HttpStatusCode.OK, sig));
        var checker = Checker(stub, spki, readLkg: () => @"Local\ROBLOX_renamed_2027");

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_renamed_2027", name);
        Assert.Equal(MutexNameSource.LastKnownGood, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_TreatsTamperedPayload_AsNoUpdate_FallsBackToDefault_WhenNoCache()
    {
        var (spki, signer) = NewKeyPair();
        var stub = new StubHttpHandler();
        var config = ConfigBytes(@"Local\ROBLOX_singletonEvent");
        var sig = Sign(signer, config);
        config[0] ^= 0xFF; // tamper AFTER signing
        stub.EnqueueResponse(Bytes(HttpStatusCode.OK, config));
        stub.EnqueueResponse(Bytes(HttpStatusCode.OK, sig));
        var checker = Checker(stub, spki, readLkg: () => null);

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(MutexHolder.DefaultMutexName, name);
        Assert.Equal(MutexNameSource.Default, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_TreatsMissingSignature_AsNoUpdate()
    {
        // Case (c): a missing .sig (404 on the sibling asset — e.g. an old cached "latest" pointer,
        // or a release published without a signer run). GetByteArrayAsync throws on non-success, so
        // this hits the SAME degrade-safe path as an offline fetch: no update available.
        var (spki, _) = NewKeyPair();
        var stub = new StubHttpHandler();
        stub.EnqueueResponse(Bytes(HttpStatusCode.OK, ConfigBytes(@"Local\ROBLOX_singletonEvent")));
        stub.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.NotFound));
        var checker = Checker(stub, spki, readLkg: () => @"Local\ROBLOX_renamed_2027");

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(@"Local\ROBLOX_renamed_2027", name);
        Assert.Equal(MutexNameSource.LastKnownGood, source);
    }

    [Fact]
    public async Task ResolveMutexNameAsync_TreatsSignatureFromWrongKey_AsNoUpdate()
    {
        // A signature that verifies fine against SOME key, just not the pinned one (e.g. a stale
        // or rotated signer). Must reject exactly like any other invalid signature.
        var (spki, _) = NewKeyPair();               // pinned key the checker trusts
        var (_, attackerSigner) = NewKeyPair();       // different key actually used to sign
        var stub = new StubHttpHandler();
        EnqueueSignedConfig(stub, attackerSigner, ConfigBytes(@"Local\ROBLOX_singletonEvent"));
        var checker = Checker(stub, spki, readLkg: () => null);

        var (name, source) = await checker.ResolveMutexNameAsync();

        Assert.Equal(MutexHolder.DefaultMutexName, name);
        Assert.Equal(MutexNameSource.Default, source);
    }

    [Fact]
    public void ResolvesViaTypedHttpClientFactory_WithExactlyOneApplicableCtor()
    {
        // Regression (smoke-caught): a SECOND public ctor made the AddHttpClient<I,T> typed-client
        // activator throw "Multiple constructors ... There should only be one applicable constructor"
        // at App startup. Direct-construction unit tests and a clean build both missed it — only the
        // real typed-client resolution path exercises ctor selection. Still true after adding the
        // optional ILogger + pinnedPublicKey params (still ONE ctor).
        var services = new ServiceCollection();
        services.AddHttpClient<IRobloxCompatChecker, RobloxCompatChecker>();
        using var provider = services.BuildServiceProvider();

        var checker = provider.GetRequiredService<IRobloxCompatChecker>();

        Assert.NotNull(checker);
    }
}
