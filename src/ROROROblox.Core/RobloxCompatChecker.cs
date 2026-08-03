using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROROROblox.Core;

/// <summary>
/// Implementation of <see cref="IRobloxCompatChecker"/>. Reads installed Roblox via
/// <c>FileVersionInfo</c> on <c>RobloxPlayerBeta.exe</c>; fetches remote config from
/// <c>GitHub Releases / latest / download / roblox-compat.json</c>. Both calls are
/// best-effort — any failure returns a no-drift result so the user sees a clean window.
///
/// <para>The remote fetch is signed. <see cref="FetchConfigAsync"/> pulls the config bytes AND the
/// detached <c>.sig</c> sibling, and verifies the raw bytes against <see cref="RobloxCompatSigningKey"/>
/// (ECDSA P-256/SHA-256, ported from 626-mod-launcher's manifest-signing pattern) BEFORE anything is
/// deserialized. Missing/invalid/unparseable signature is treated exactly like a network failure —
/// no update available, logged locally at Debug, never a crash, and the unverified bytes are never
/// deserialized or cached. Stricter than the mod-launcher's cache-then-verify-at-next-launch design:
/// this feed verifies at fetch time, in memory, so nothing unverified is ever trusted.</para>
/// </summary>
public sealed class RobloxCompatChecker : IRobloxCompatChecker
{
    private const string CompatConfigUrl =
        "https://github.com/estevanhernandez-stack-ed/ROROROblox/releases/latest/download/roblox-compat.json";

    private const string CompatConfigSignatureUrl = CompatConfigUrl + ".sig";

    private const string IssuesUrl =
        "https://github.com/estevanhernandez-stack-ed/ROROROblox/issues";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string LastKnownMutexFileName = "last-known-mutex.txt";

    private readonly HttpClient _httpClient;
    private readonly Func<string?> _readLastKnownMutex;
    private readonly Action<string> _writeLastKnownMutex;
    private readonly ILogger<RobloxCompatChecker> _log;
    private readonly byte[] _pinnedPublicKey;

    /// <summary>
    /// ONE public ctor only — the typed-HttpClient DI activator requires exactly one applicable
    /// constructor (two would make it throw "Multiple constructors" at resolve time). The DI
    /// registration supplies just the <see cref="HttpClient"/> (DI fills the optional
    /// <see cref="ILogger{T}"/>); the last-known-good cache read/write default to the real
    /// <c>%LOCALAPPDATA%\ROROROblox\last-known-mutex.txt</c> seams, and <paramref name="pinnedPublicKey"/>
    /// defaults to the pinned production key (<see cref="RobloxCompatSigningKey.PublicKeySpki"/>).
    /// Unit tests pass fakes/an explicit test keypair to drive the resolver's fallback ladder and the
    /// signature-verify gate without touching disk, the network, or the production private key
    /// (which lives only in CI).
    /// </summary>
    public RobloxCompatChecker(
        HttpClient httpClient,
        Func<string?>? readLastKnownMutex = null,
        Action<string>? writeLastKnownMutex = null,
        ILogger<RobloxCompatChecker>? log = null,
        byte[]? pinnedPublicKey = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _readLastKnownMutex = readLastKnownMutex ?? ReadLastKnownMutexFromDisk;
        _writeLastKnownMutex = writeLastKnownMutex ?? WriteLastKnownMutexToDisk;
        _log = log ?? NullLogger<RobloxCompatChecker>.Instance;
        _pinnedPublicKey = pinnedPublicKey ?? RobloxCompatSigningKey.PublicKeySpki;
    }

    public async Task<CompatCheckResult> CheckAsync()
    {
        var installed = GetInstalledRobloxVersion();
        if (installed is null)
        {
            // Roblox isn't installed — banner is item 9's "Roblox not installed" modal,
            // not the version-drift banner.
            return new CompatCheckResult(HasDrift: false, Banner: null);
        }

        var config = await FetchConfigAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
        if (config is null)
        {
            // No network or no published config — fail-quiet. Returning no-drift means the user
            // doesn't see a stale banner. If multi-instance breaks, the symptoms still point them
            // at the issue tracker via the "Roblox not installed" / "session expired" surfaces.
            return new CompatCheckResult(HasDrift: false, Banner: null);
        }

        if (!Version.TryParse(installed, out var installedVer)
            || !Version.TryParse(config.KnownGoodVersionMin, out var minVer)
            || !Version.TryParse(config.KnownGoodVersionMax, out var maxVer))
        {
            return new CompatCheckResult(HasDrift: false, Banner: null);
        }

        if (installedVer >= minVer && installedVer <= maxVer)
        {
            return new CompatCheckResult(HasDrift: false, Banner: null);
        }

        var direction = installedVer > maxVer ? "updated to" : "downgraded to";
        var banner =
            $"Roblox {direction} {installed}. We've tested up to {config.KnownGoodVersionMax}. " +
            $"Multi-instance might not work — let us know at {IssuesUrl}.";

        return new CompatCheckResult(HasDrift: true, Banner: banner);
    }

    private async Task<RobloxCompatConfig?> FetchConfigAsync(TimeSpan timeout)
    {
        byte[] configBytes;
        byte[] signatureBytes;
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            configBytes = await _httpClient.GetByteArrayAsync(CompatConfigUrl, cts.Token).ConfigureAwait(false);
            signatureBytes = await _httpClient.GetByteArrayAsync(CompatConfigSignatureUrl, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // No network, timeout, or missing .sig (404 -> HttpRequestException). Fail-quiet: treated
            // exactly like "no published config" — the caller sees no drift / the default mutex name.
            _log.LogDebug(ex, "roblox-compat.json/.sig fetch failed; degrading to no update available.");
            return null;
        }

        // Verify the EXACT bytes fetched off the wire, before anything is deserialized. Reject-and-
        // fail-quiet: an invalid/missing signature is never distinguished from a network failure to
        // the caller, and the unverified bytes are never deserialized, cached, or trusted.
        if (!RobloxCompatSignature.Verify(_pinnedPublicKey, configBytes, signatureBytes))
        {
            _log.LogDebug("roblox-compat.json signature did not verify; degrading to no update available.");
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RobloxCompatConfig>(configBytes, JsonOptions);
        }
        catch (JsonException ex)
        {
            _log.LogDebug(ex, "roblox-compat.json failed to parse after a valid signature; degrading to no update available.");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<(string Name, MutexNameSource Source)> ResolveMutexNameAsync()
    {
        try
        {
            // Own 2s budget (vs the 8s banner fetch) so name resolution can run before mutex.Acquire
            // without holding first paint hostage to the network.
            var config = await FetchConfigAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            if (config is not null && MutexHolder.IsValidName(config.MutexName))
            {
                TryPersistLastKnown(config.MutexName);
                return (config.MutexName, MutexNameSource.RemoteConfig);
            }

            var cached = TryReadLastKnown();
            if (MutexHolder.IsValidName(cached))
            {
                return (cached!, MutexNameSource.LastKnownGood);
            }

            return (MutexHolder.DefaultMutexName, MutexNameSource.Default);
        }
        catch
        {
            // The resolver promises no-throw; any unexpected failure binds the safe default so a
            // broken roblox-compat.json can never brick multi-instance.
            return (MutexHolder.DefaultMutexName, MutexNameSource.Default);
        }
    }

    private string? TryReadLastKnown()
    {
        try
        {
            return _readLastKnownMutex();
        }
        catch
        {
            return null;
        }
    }

    private void TryPersistLastKnown(string name)
    {
        try
        {
            _writeLastKnownMutex(name);
        }
        catch
        {
            // Best-effort cache. A persist failure must never break resolution.
        }
    }

    private static string LastKnownMutexPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ROROROblox",
        LastKnownMutexFileName);

    private static string? ReadLastKnownMutexFromDisk()
    {
        var path = LastKnownMutexPath;
        return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
    }

    private static void WriteLastKnownMutexToDisk(string name)
    {
        var path = LastKnownMutexPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, name);
    }

    /// <summary>
    /// Reads the file-version string of the newest installed <c>RobloxPlayerBeta.exe</c>
    /// (the current <c>version-*</c> dir). Returns <c>null</c> when Roblox isn't installed or the
    /// read fails. Exposed <c>internal</c> so the v1.7.0 <c>RobloxUpdateProbe</c> reuses the exact
    /// same installed-version read (spec §"Components > 1. Update-pending detection").
    /// </summary>
    internal static string? GetInstalledRobloxVersion()
    {
        try
        {
            var versionsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Roblox",
                "Versions");

            if (!Directory.Exists(versionsDir))
            {
                return null;
            }

            var folders = new DirectoryInfo(versionsDir)
                .GetDirectories("version-*")
                .OrderByDescending(d => d.LastWriteTimeUtc);

            foreach (var folder in folders)
            {
                var exePath = Path.Combine(folder.FullName, "RobloxPlayerBeta.exe");
                if (!File.Exists(exePath))
                {
                    continue;
                }
                var info = FileVersionInfo.GetVersionInfo(exePath);
                return info.FileVersion ?? info.ProductVersion;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
