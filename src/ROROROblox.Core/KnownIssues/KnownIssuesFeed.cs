using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core.KnownIssues;

public enum KnownIssuesRefreshKind
{
    Updated,
    NetworkFailed,
    SignatureRejected,
    Invalid,
    TooLarge,
}

public sealed record KnownIssuesFetch(
    KnownIssuesRefreshKind Kind,
    KnownIssuesDocument? Document,
    IReadOnlyList<KnownIssuesProblem> Problems);

public interface IKnownIssuesFeed
{
    /// <summary>The saved copy, if it still verifies and validates; otherwise null. Never throws.</summary>
    KnownIssuesDocument? LoadCache();

    /// <summary>Downloads, verifies and validates the published file; caches it only on <see cref="KnownIssuesRefreshKind.Updated"/>.</summary>
    Task<KnownIssuesFetch> FetchAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Known Roblox issues, signed like <c>roblox-compat.json</c> (see <see cref="RobloxCompatChecker"/>):
/// the raw bytes are verified against <see cref="RobloxCompatSigningKey"/> before anything is parsed,
/// and nothing unverified is parsed, cached or trusted. Stateless and transient — the snapshot lives in
/// <see cref="KnownIssuesState"/>.
/// <para>
/// ONE public constructor: the typed-HttpClient activator requires exactly one applicable constructor
/// (<c>TypedHttpClientRegistrationTests</c>). DI supplies the <see cref="HttpClient"/> and the logger;
/// tests pass a scratch cache folder and an ephemeral key.
/// </para>
/// </summary>
public sealed class KnownIssuesFeed : IKnownIssuesFeed
{
    public const string FeedUrl =
        "https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/known-issues.json";

    public const string SignatureUrl = FeedUrl + ".sig";

    /// <summary>256 KB — far above any real list. Checked before the signature is fetched.</summary>
    public const int MaxDocumentBytes = 256 * 1024;

    /// <summary>A P-256 P1363 signature is 64 bytes; anything past 1 KB is not a signature.</summary>
    private const int MaxSignatureBytes = 1024;

    internal const string CacheFileName = "known-issues.cache.json";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;
    private readonly ILogger<KnownIssuesFeed> _log;
    private readonly string _cacheDirectory;
    private readonly byte[] _pinnedPublicKey;

    public KnownIssuesFeed(
        HttpClient httpClient,
        ILogger<KnownIssuesFeed>? log = null,
        string? cacheDirectory = null,
        byte[]? pinnedPublicKey = null)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _log = log ?? NullLogger<KnownIssuesFeed>.Instance;
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ROROROblox");
        _pinnedPublicKey = pinnedPublicKey ?? RobloxCompatSigningKey.PublicKeySpki;
    }

    private string CachePath => Path.Combine(_cacheDirectory, CacheFileName);

    private string CacheSignaturePath => CachePath + ".sig";

    public KnownIssuesDocument? LoadCache()
    {
        byte[] body;
        byte[] signature;
        try
        {
            if (!File.Exists(CachePath) || !File.Exists(CacheSignaturePath))
            {
                _log.LogDebug("Known issues: no saved copy yet.");
                return null;
            }

            body = File.ReadAllBytes(CachePath);
            signature = File.ReadAllBytes(CacheSignaturePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Known issues: the saved copy could not be read; ignoring it.");
            return null;
        }

        if (!RobloxCompatSignature.Verify(_pinnedPublicKey, body, signature))
        {
            _log.LogWarning("Known issues: the saved copy failed its signature check; ignoring it.");
            return null;
        }

        var parsed = KnownIssuesParser.Parse(body);
        if (!parsed.IsValid)
        {
            _log.LogWarning(
                "Known issues: the saved copy is signed but not valid ({Problems}); ignoring it.",
                Describe(parsed.Problems));
            return null;
        }

        return parsed.Document;
    }

    public async Task<KnownIssuesFetch> FetchAsync(CancellationToken cancellationToken = default)
    {
        byte[]? body;
        byte[]? signature;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);

            body = await GetLimitedAsync(FeedUrl, MaxDocumentBytes, cts.Token).ConfigureAwait(false);
            if (body is null)
            {
                _log.LogWarning(
                    "Known issues: the published file is larger than {Limit} bytes; refused before checking its signature.",
                    MaxDocumentBytes);
                return new(KnownIssuesRefreshKind.TooLarge, null, []);
            }

            signature = await GetLimitedAsync(SignatureUrl, MaxSignatureBytes, cts.Token).ConfigureAwait(false);
            if (signature is null)
            {
                _log.LogWarning("Known issues: the published signature is larger than {Limit} bytes; refused.", MaxSignatureBytes);
                return new(KnownIssuesRefreshKind.TooLarge, null, []);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _log.LogDebug(ex, "Known issues: could not reach the release; keeping what we have.");
            return new(KnownIssuesRefreshKind.NetworkFailed, null, []);
        }

        if (!RobloxCompatSignature.Verify(_pinnedPublicKey, body, signature))
        {
            _log.LogWarning("Known issues: the published file's signature did not verify; keeping what we have.");
            return new(KnownIssuesRefreshKind.SignatureRejected, null, []);
        }

        var parsed = KnownIssuesParser.Parse(body);
        if (!parsed.IsValid)
        {
            _log.LogWarning(
                "Known issues: the published file is signed but not valid ({Problems}); keeping what we have.",
                Describe(parsed.Problems));
            return new(KnownIssuesRefreshKind.Invalid, null, parsed.Problems);
        }

        TryWriteCache(body, signature);
        return new(KnownIssuesRefreshKind.Updated, parsed.Document, []);
    }

    /// <summary>The body, or null when it is larger than <paramref name="limit"/>. Streams, so an oversized body is never buffered whole.</summary>
    private async Task<byte[]?> GetLimitedAsync(string url, int limit, CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long declared && declared > limit)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private void TryWriteCache(byte[] body, byte[] signature)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var tempBody = CachePath + ".tmp";
            var tempSignature = CacheSignaturePath + ".tmp";
            File.WriteAllBytes(tempBody, body);
            File.WriteAllBytes(tempSignature, signature);
            File.Move(tempBody, CachePath, overwrite: true);
            File.Move(tempSignature, CacheSignaturePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Known issues: could not save a copy for next time; the list still shows this session.");
        }
    }

    private static string Describe(IReadOnlyList<KnownIssuesProblem> problems) =>
        string.Join(", ", problems.Select(p =>
            p.Kind + (p.IssueId is null ? string.Empty : $" '{p.IssueId}'") + (p.Field is null ? string.Empty : $" {p.Field}")));
}
