using System.Net;
using System.Security.Cryptography;
using System.Text;
using ROROROblox.Core;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Tests.KnownIssues;

/// <summary>
/// The feed is signed exactly like <c>roblox-compat.json</c>: the signature covers the raw downloaded
/// bytes and nothing is parsed before it verifies. Every row of the spec's §2 failure table has a test
/// here, including the log level it writes. Ephemeral test keypairs only — the production private key
/// lives in CI.
/// </summary>
public sealed class KnownIssuesFeedTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "rororo-known-issues-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly StubHttpHandler _stub = new();
    private readonly CapturingLogger<KnownIssuesFeed> _log = new();

    private static readonly byte[] ValidBody = Encoding.UTF8.GetBytes("""
        { "schemaVersion": 1, "issues": [ { "id": "a", "postedAt": "2026-09-23", "notify": true,
          "title": "t", "symptom": "s", "workaround": "w" } ] }
        """);

    public void Dispose()
    {
        _signer.Dispose();
        try { if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true); } catch (IOException) { }
    }

    private KnownIssuesFeed Feed() =>
        new(new HttpClient(_stub), _log, _cacheDir, _signer.ExportSubjectPublicKeyInfo());

    private byte[] Sign(byte[] data) => _signer.SignData(data, HashAlgorithmName.SHA256, RobloxCompatSignature.Format);

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private void EnqueueSigned(byte[] body)
    {
        _stub.EnqueueResponse(Ok(body));
        _stub.EnqueueResponse(Ok(Sign(body)));
    }

    private string CachePath => Path.Combine(_cacheDir, KnownIssuesFeed.CacheFileName);

    [Fact]
    public async Task AVerifiedValidFileIsReturnedAndSavedForNextTime()
    {
        EnqueueSigned(ValidBody);

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.Updated, fetch.Kind);
        Assert.Equal("a", Assert.Single(fetch.Document!.Issues).Id);
        Assert.Equal("a", Assert.Single(Feed().LoadCache()!.Issues).Id);
    }

    [Fact]
    public async Task ItAsksForTheFileAndItsSignatureFromTheLatestRelease()
    {
        EnqueueSigned(ValidBody);

        await Feed().FetchAsync();

        Assert.Equal(KnownIssuesFeed.FeedUrl, _stub.Requests[0].RequestUri!.ToString());
        Assert.Equal(KnownIssuesFeed.SignatureUrl, _stub.Requests[1].RequestUri!.ToString());
    }

    [Fact]
    public async Task ASignatureThatDoesNotVerifyIsRejected_NeverCached_AndLoggedAsAWarning()
    {
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _stub.EnqueueResponse(Ok(ValidBody));
        _stub.EnqueueResponse(Ok(stranger.SignData(ValidBody, HashAlgorithmName.SHA256, RobloxCompatSignature.Format)));

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.SignatureRejected, fetch.Kind);
        Assert.Null(fetch.Document);
        Assert.False(File.Exists(CachePath));
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Warning]") && l.Contains("signature did not verify"));
    }

    [Fact]
    public async Task ASignedFileInANewerFormatIsInvalid_NotCached_AndLoggedAsAWarning()
    {
        EnqueueSigned(Encoding.UTF8.GetBytes("{ \"schemaVersion\": 2, \"issues\": [] }"));

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.Invalid, fetch.Kind);
        Assert.Contains(fetch.Problems, p => p.Kind == KnownIssuesProblemKind.UnsupportedSchemaVersion);
        Assert.False(File.Exists(CachePath));
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Warning]") && l.Contains("not valid"));
    }

    [Fact]
    public async Task NoNetworkIsANetworkFailure_LoggedAtDebug()
    {
        _stub.EnqueueResponse(_ => throw new HttpRequestException("offline"));

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.NetworkFailed, fetch.Kind);
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Debug]") && l.Contains("could not reach"));
    }

    [Fact]
    public async Task AMissingSignatureIsANetworkFailure()
    {
        _stub.EnqueueResponse(Ok(ValidBody));
        _stub.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Equal(KnownIssuesRefreshKind.NetworkFailed, (await Feed().FetchAsync()).Kind);
    }

    /// <summary>Refused before the signature is fetched, let alone checked: one request, not two.</summary>
    [Fact]
    public async Task AnOversizedFileIsRefusedBeforeItsSignatureIsChecked()
    {
        _stub.EnqueueResponse(Ok(new byte[KnownIssuesFeed.MaxDocumentBytes + 1]));

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.TooLarge, fetch.Kind);
        Assert.Single(_stub.Requests);
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Warning]") && l.Contains("larger than"));
    }

    [Fact]
    public void WithNoSavedCopyLoadCacheReturnsNothing_AtDebug()
    {
        Assert.Null(Feed().LoadCache());
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Debug]") && l.Contains("no saved copy"));
    }

    /// <summary>
    /// The guarantee the cache rests on: a copy on disk is trusted only if it still verifies. Proven by
    /// breaking the check on purpose (Task 3, Step 5) and watching this fail.
    /// </summary>
    [Fact]
    public async Task ChangingOneByteOfTheSavedCopyGetsItIgnored()
    {
        EnqueueSigned(ValidBody);
        await Feed().FetchAsync();

        var bytes = File.ReadAllBytes(CachePath);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(CachePath, bytes);

        Assert.Null(Feed().LoadCache());
        Assert.Contains(_log.Snapshot(), l => l.StartsWith("[Warning]") && l.Contains("failed its signature check"));
    }

    /// <summary>Review Focus 3: a crash mid-write leaves half a file. Ignored, never thrown.</summary>
    [Fact]
    public async Task ATruncatedSavedCopyIsIgnoredWithoutThrowing()
    {
        EnqueueSigned(ValidBody);
        await Feed().FetchAsync();

        var bytes = File.ReadAllBytes(CachePath);
        File.WriteAllBytes(CachePath, bytes[..(bytes.Length / 2)]);

        Assert.Null(Feed().LoadCache());
    }

    [Fact]
    public async Task AValidEmptyListIsAnUpdate_ItIsHowEveryIssueIsRetired()
    {
        EnqueueSigned(Encoding.UTF8.GetBytes("{ \"schemaVersion\": 1, \"issues\": [] }"));

        var fetch = await Feed().FetchAsync();

        Assert.Equal(KnownIssuesRefreshKind.Updated, fetch.Kind);
        Assert.Empty(fetch.Document!.Issues);
    }
}
