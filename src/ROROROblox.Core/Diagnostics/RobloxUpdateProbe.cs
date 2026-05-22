using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core.Diagnostics;

/// <summary>
/// Default <see cref="IRobloxUpdateProbe"/> implementation — the v1.7.0 install-deferral
/// foundational signal (spec §"Components > 1. Update-pending detection"). Two posture-clean
/// reads: one process scan for <c>RobloxPlayerInstaller.exe</c> and one documented GET against
/// <c>clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer</c>. No bootstrapper / handler
/// takeover, no version management.
/// </summary>
/// <remarks>
/// All three signals are injected seams so tests never need a real process or live network:
/// the installer-running scan (<c>Func&lt;bool&gt;</c>), the installed-version provider
/// (defaults to <see cref="RobloxCompatChecker.GetInstalledRobloxVersion"/> — the SAME file-version
/// read the compat banner uses), and the latest-version fetch (the real CDN GET via the injected
/// <see cref="HttpClient"/>). Every member is degrade-safe: on ANY failure it returns the
/// "don't block the launch" answer (<c>false</c>).
///
/// <para><b>Like-for-like comparison.</b> <see cref="RobloxCompatChecker.GetInstalledRobloxVersion"/>
/// returns the <c>FileVersion</c> dotted-quad of the newest installed <c>RobloxPlayerBeta.exe</c>
/// (e.g. <c>2.661.0.6610701</c>) — NOT the <c>version-*</c> GUID. The CDN response carries two
/// fields: <c>version</c> (the matching file-version string) and <c>clientVersionUpload</c> (the
/// GUID). We therefore compare the installed file version against the CDN's <c>version</c> field —
/// like-for-like. The slate's mention of <c>clientVersionUpload</c> assumed the installed read
/// returned the GUID; it doesn't, so <c>version</c> is the correct counterpart.</para>
/// </remarks>
public sealed class RobloxUpdateProbe : IRobloxUpdateProbe
{
    private const string InstallerProcessName = "RobloxPlayerInstaller";
    private const string ClientVersionUrl =
        "https://clientsettingscdn.roblox.com/v2/client-version/WindowsPlayer";
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(8);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Func<bool> _installerRunning;
    private readonly Func<string?> _installedVersionProvider;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RobloxUpdateProbe> _log;

    /// <summary>
    /// Convenience ctor for the composition root — wires the real seams (live process scan +
    /// <see cref="RobloxCompatChecker.GetInstalledRobloxVersion"/>) with a NullLogger default,
    /// mirroring the other Core diagnostics. The <see cref="HttpClient"/> comes from
    /// <c>IHttpClientFactory</c> at the composition root (same pattern as <see cref="RobloxApi"/>).
    /// </summary>
    public RobloxUpdateProbe(HttpClient httpClient)
        : this(httpClient, NullLogger<RobloxUpdateProbe>.Instance) { }

    public RobloxUpdateProbe(HttpClient httpClient, ILogger<RobloxUpdateProbe> log)
        : this(
            installerRunning: DefaultInstallerScan,
            installedVersionProvider: RobloxCompatChecker.GetInstalledRobloxVersion,
            httpClient: httpClient,
            log: log)
    { }

    /// <summary>
    /// Seam ctor — inject the installer-running scan, the installed-version provider, and the
    /// <see cref="HttpClient"/> backing the CDN GET. Used by tests; also the place DI can override
    /// any single seam without a real process or live network.
    /// </summary>
    public RobloxUpdateProbe(
        Func<bool> installerRunning,
        Func<string?> installedVersionProvider,
        HttpClient httpClient,
        ILogger<RobloxUpdateProbe>? log = null)
    {
        _installerRunning = installerRunning ?? throw new ArgumentNullException(nameof(installerRunning));
        _installedVersionProvider = installedVersionProvider ?? throw new ArgumentNullException(nameof(installedVersionProvider));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _log = log ?? NullLogger<RobloxUpdateProbe>.Instance;
        EnsureUserAgent(_httpClient);
    }

    public bool IsInstallerRunning()
    {
        try
        {
            return _installerRunning();
        }
        catch (Exception ex)
        {
            // Degrade-safe: a scan failure must never block a launch. Treat as "not installing".
            _log.LogDebug(ex, "RobloxPlayerInstaller scan failed; treating as not running.");
            return false;
        }
    }

    public async Task<bool> IsUpdatePendingAsync(CancellationToken ct = default)
    {
        string? installed;
        try
        {
            installed = _installedVersionProvider();
        }
        catch (Exception ex)
        {
            // Installed-version read failed (disk, permissions). Don't block — degrade to "no update".
            _log.LogDebug(ex, "Installed-version read failed; treating as no update pending.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(installed))
        {
            // Roblox not installed (or unreadable) — there's nothing to defer. Item 9 owns the
            // "Roblox not installed" surface; the probe just declines to block.
            return false;
        }

        var latest = await FetchLatestVersionAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(latest))
        {
            // No network / non-200 / parse failure / missing-or-empty version field. Degrade to
            // "no update pending" so a CDN hiccup never stalls a launch (spec risks: CDN endpoint).
            return false;
        }

        // Like-for-like string compare (file version vs CDN `version`). Case-insensitive + trimmed
        // to absorb cosmetic CDN formatting. Differ => an update is pending.
        return !string.Equals(installed.Trim(), latest.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> FetchLatestVersionAsync(CancellationToken ct)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(FetchTimeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, ClientVersionUrl);
            using var response = await _httpClient.SendAsync(request, linkedCts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogDebug("client-version endpoint returned {Status}; degrading to no-update.", (int)response.StatusCode);
                return null;
            }

            var payload = await response.Content
                .ReadFromJsonAsync<ClientVersionResponse>(JsonOptions, linkedCts.Token)
                .ConfigureAwait(false);
            return payload?.Version;
        }
        catch (Exception ex)
        {
            // Network down, timeout, malformed JSON — all degrade-safe to null => no update pending.
            _log.LogDebug(ex, "client-version fetch/parse failed; degrading to no-update.");
            return null;
        }
    }

    private static bool DefaultInstallerScan()
    {
        var processes = Process.GetProcessesByName(InstallerProcessName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            // Dispose every handle — same anti-leak discipline as RobloxRunningProbe / tracker.
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }

    private static void EnsureUserAgent(HttpClient client)
    {
        // ROROROblox/<version> — never a browser spoof (CLAUDE.md hard rule + Store narrative).
        // Idempotent: a shared HttpClient may already carry the UA from RobloxApi's ctor.
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            var version = typeof(RobloxUpdateProbe).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RORORO", version));
        }
    }

    // Wire-shape record for the CDN response. `version` is the file-version string (matches what
    // GetInstalledRobloxVersion returns); `clientVersionUpload` is the version-* GUID (unused here).
    private sealed record ClientVersionResponse(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("clientVersionUpload")] string? ClientVersionUpload);
}
