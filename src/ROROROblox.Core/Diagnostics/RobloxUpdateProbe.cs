using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ROROROblox.Core.KnownIssues;

namespace ROROROblox.Core.Diagnostics;

/// <summary>How a local Roblox version string compares with the endpoint's, once both are parsed.</summary>
internal enum VersionMatch { Unknown, Same, LocalOlder, LocalNewer }

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
/// <para><b>Parsed comparison (since v1.41).</b> The local file version is compared against the
/// CDN's <c>version</c> field (not <c>clientVersionUpload</c>, the GUID). Both sides go through
/// <see cref="RobloxVersion.TryParseInstalled"/> first: the file version reads with commas and
/// spaces (<c>0, 748, 0, 7481526</c>) while the endpoint answers dotted (<c>0.748.0.7481526</c>),
/// so the string compare this class did through v1.40 reported an update that was never there.
/// An unreadable string is unknown, never a difference.</para>
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
    private readonly Func<string?> _handlerVersionProvider;
    private readonly Func<int> _recentInstallCounter;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RobloxUpdateProbe> _log;

    /// <summary>
    /// ONE public ctor only — the typed-HttpClient DI activator requires exactly one applicable
    /// constructor for <c>[HttpClient]</c>; a second applicable overload (e.g. a
    /// <c>(HttpClient, ILogger&lt;T&gt;)</c> form, since <c>AddLogging</c> registers <c>ILogger&lt;T&gt;</c>)
    /// makes it throw "Multiple constructors" at resolve time and crashes startup. The composition
    /// root supplies just the <see cref="HttpClient"/> (DI fills the optional <see cref="ILogger{T}"/>);
    /// the installer-running scan and installed-version provider default to the real seams (live
    /// process scan + <see cref="RobloxCompatChecker.GetInstalledRobloxVersion"/>, the same file-version
    /// read the compat banner uses). Tests pass fakes via named arguments to drive each degrade-safe
    /// branch without a real process or live network.
    /// </summary>
    public RobloxUpdateProbe(
        HttpClient httpClient,
        Func<bool>? installerRunning = null,
        Func<string?>? installedVersionProvider = null,
        Func<string?>? handlerVersionProvider = null,
        Func<int>? recentInstallCounter = null,
        ILogger<RobloxUpdateProbe>? log = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _installerRunning = installerRunning ?? DefaultInstallerScan;
        _installedVersionProvider = installedVersionProvider ?? RobloxCompatChecker.GetInstalledRobloxVersion;
        _handlerVersionProvider = handlerVersionProvider ?? RobloxCompatChecker.GetHandlerRobloxVersion;
        _recentInstallCounter = recentInstallCounter
            ?? (() => RobloxCompatChecker.CountRecentVersionInstalls(RobloxCompatChecker.DefaultChurnWindow));
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
        => (await GetUpdateStateAsync(ct).ConfigureAwait(false)).Kind == RobloxUpdateKind.Pending;

    public async Task<RobloxUpdateState> GetUpdateStateAsync(CancellationToken ct = default)
    {
        // Two local reads. The handler's pin is what a launch will actually run; the newest install
        // is what used to be read here alone, and is kept because it still catches the case where
        // the handler is unreadable or a strap owns it.
        var handler = ReadVersionSafe(_handlerVersionProvider, "handler");
        var installed = ReadVersionSafe(_installedVersionProvider, "newest-installed");

        if (string.IsNullOrWhiteSpace(handler) && string.IsNullOrWhiteSpace(installed))
        {
            // Roblox not installed (or nothing readable): nothing to defer, and no spent round-trip.
            LogState(handler, installed, null, RobloxUpdateState.Unknown);
            return RobloxUpdateState.Unknown;
        }

        var latestText = await FetchLatestVersionAsync(ct).ConfigureAwait(false);
        RobloxUpdateState state;
        if (!RobloxVersion.TryParseInstalled(latestText, out var latest))
        {
            // No network / non-200 / parse failure / blank or unparseable version field. Degrade to
            // Unknown so a CDN hiccup never stalls a launch.
            state = RobloxUpdateState.Unknown;
        }
        else
        {
            state = Decide(handler, installed, latest);
        }

        LogState(handler, installed, latestText, state);
        return state;
    }

    public RobloxUpdateState CompareInstalledTo(Version latest)
    {
        var handler = ReadVersionSafe(_handlerVersionProvider, "handler");
        var installed = ReadVersionSafe(_installedVersionProvider, "newest-installed");
        return Decide(handler, installed, latest);
    }

    public bool IsUpdateChurnActive()
    {
        try
        {
            // More than one install inside the window means updates are landing on top of each
            // other. Exactly one is an ordinary update and the version comparison above owns it.
            return _recentInstallCounter() > 1;
        }
        catch (Exception ex)
        {
            // Degrade-safe, same as every other member: a failed count never holds a batch.
            _log.LogDebug(ex, "Recent-install count failed; treating as no churn.");
            return false;
        }
    }

    private static RobloxUpdateState Decide(string? handler, string? installed, Version latest)
    {
        var handlerMatch = Compare(handler, latest);
        var installedMatch = Compare(installed, latest);

        if (handlerMatch == VersionMatch.Unknown && installedMatch == VersionMatch.Unknown)
        {
            return RobloxUpdateState.Unknown;
        }

        if (handlerMatch is VersionMatch.Unknown or VersionMatch.Same
            && installedMatch is VersionMatch.Unknown or VersionMatch.Same)
        {
            return new RobloxUpdateState(RobloxUpdateKind.Current, RobloxUpdateDirection.None, latest);
        }

        // Direction: the handler's read when it is readable and differs (it is what a launch runs),
        // otherwise the newest install's.
        var deciding = handlerMatch is VersionMatch.LocalOlder or VersionMatch.LocalNewer ? handlerMatch : installedMatch;
        var direction = deciding == VersionMatch.LocalOlder ? RobloxUpdateDirection.Newer : RobloxUpdateDirection.Older;
        return new RobloxUpdateState(RobloxUpdateKind.Pending, direction, latest);
    }

    /// <summary>
    /// Compares parsed versions. Both sides go through <see cref="RobloxVersion.TryParseInstalled"/>, so
    /// <c>0, 748, 0, 7481526</c> and <c>0.748.0.7481526</c> are the same version. Blank or unparseable on
    /// either side is <see cref="VersionMatch.Unknown"/>, which never votes.
    /// </summary>
    internal static VersionMatch Compare(string? local, string? latest)
        => RobloxVersion.TryParseInstalled(local, out var l) && RobloxVersion.TryParseInstalled(latest, out var r)
            ? Compare(l, r)
            : VersionMatch.Unknown;

    private static VersionMatch Compare(string? local, Version latest)
        => RobloxVersion.TryParseInstalled(local, out var l) ? Compare(l, latest) : VersionMatch.Unknown;

    private static VersionMatch Compare(Version local, Version latest)
    {
        var order = local.CompareTo(latest);
        return order == 0 ? VersionMatch.Same : order < 0 ? VersionMatch.LocalOlder : VersionMatch.LocalNewer;
    }

    private void LogState(string? handler, string? installed, string? latest, RobloxUpdateState state)
        => _log.LogDebug(
            "client-version check: handler {Handler}, installed {Installed}, latest {Latest} -> {Kind} {Direction}",
            handler, installed, latest, state.Kind, state.Direction);

    private string? ReadVersionSafe(Func<string?> provider, string label)
    {
        try
        {
            return provider();
        }
        catch (Exception ex)
        {
            // Disk, registry, permissions. Treat as unknown; the other read may still answer.
            _log.LogDebug(ex, "{Label} version read failed; treating as unknown.", label);
            return null;
        }
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
        // RORORO/<version> — never a browser spoof (CLAUDE.md hard rule + Store narrative). The
        // product token matches RobloxApi's ctor; ruled the contract 2026-08-30.
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
